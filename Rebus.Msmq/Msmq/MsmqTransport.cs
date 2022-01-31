﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Bus;
using Rebus.Exceptions;
using Rebus.Extensions;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Serialization;
using Rebus.Transport;
using Message = System.Messaging.Message;
#pragma warning disable 1998

namespace Rebus.Msmq
{
    /// <summary>
    /// Implementation of <see cref="ITransport"/> that uses MSMQ to do its thing
    /// </summary>
    public class MsmqTransport : ITransport, IInitializable, IDisposable
    {
        const string CurrentTransactionKey = "msmqtransport-messagequeuetransaction";
        const string CurrentOutgoingQueuesKey = "msmqtransport-outgoing-messagequeues";
        readonly List<Action<MessageQueue>> _newQueueCallbacks = new List<Action<MessageQueue>>();
        readonly IMsmqHeaderSerializer _msmqHeaderSerializer;
        readonly string _inputQueueName;
        readonly ILog _log;

        volatile MessageQueue _inputQueue;
        bool _disposed;

        /// <summary>
        /// Constructs the transport with the specified input queue address
        /// </summary>
        public MsmqTransport(string inputQueueAddress, IRebusLoggerFactory rebusLoggerFactory, IMsmqHeaderSerializer msmqHeaderSerializer)
        {
            if (rebusLoggerFactory == null) throw new ArgumentNullException(nameof(rebusLoggerFactory));
            _msmqHeaderSerializer = msmqHeaderSerializer ?? throw new ArgumentNullException(nameof(msmqHeaderSerializer));

            _log = rebusLoggerFactory.GetLogger<MsmqTransport>();

            if (inputQueueAddress != null)
            {
                _inputQueueName = MakeGloballyAddressable(inputQueueAddress);
            }
        }

        /// <summary>
        /// Adds a callback to be invoked when a new queue is created. Can be used e.g. to customize permissions
        /// </summary>
        public void AddQueueCallback(Action<MessageQueue> callback)
        {
            _newQueueCallbacks.Add(callback);
        }

        static string MakeGloballyAddressable(string inputQueueName)
        {
            return inputQueueName.Contains("@")
                ? inputQueueName
                : $"{inputQueueName}@{Environment.MachineName}";
        }

        /// <summary>
        /// Initializes the transport by creating the input queue
        /// </summary>
        public void Initialize()
        {
            if (_inputQueueName != null)
            {
                _log.Info("Initializing MSMQ transport - input queue: {queueName}", _inputQueueName);

                GetInputQueue();
            }
            else
            {
                _log.Info("Initializing one-way MSMQ transport");
            }
        }

        /// <summary>
        /// Creates a queue with the given address, unless the address is of a remote queue - in that case,
        /// this call is ignored
        /// </summary>
        public void CreateQueue(string address)
        {
            if (!MsmqUtil.IsLocal(address)) return;

            var inputQueuePath = MsmqUtil.GetPath(address);

            if (_newQueueCallbacks.Any())
            {
                MsmqUtil.EnsureQueueExists(inputQueuePath, _log, messageQueue =>
                {
                    _newQueueCallbacks.ForEach(callback => callback(messageQueue));
                });
            }
            else
            {
                MsmqUtil.EnsureQueueExists(inputQueuePath, _log);
            }

            MsmqUtil.EnsureMessageQueueIsTransactional(inputQueuePath);
        }

        /// <summary>
        /// Deletes all messages in the input queue
        /// </summary>
        public void PurgeInputQueue()
        {
            if (!MsmqUtil.QueueExists(_inputQueueName))
            {
                _log.Info("Purging {queueName} (but the queue doesn't exist...)", _inputQueueName);
                return;
            }

            _log.Info("Purging {queueName}", _inputQueueName);
            MsmqUtil.PurgeQueue(_inputQueueName);
        }

        /// <summary>
        /// Sends the given transport message to the specified destination address using MSMQ. Will use the existing <see cref="MessageQueueTransaction"/> stashed
        /// under the <see cref="CurrentTransactionKey"/> key in the given <paramref name="context"/>, or else it will create one and add it.
        /// </summary>
        public async Task Send(string destinationAddress, TransportMessage message, ITransactionContext context)
        {
            if (destinationAddress == null) throw new ArgumentNullException(nameof(destinationAddress));
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var logicalMessage = CreateMsmqMessage(message);

            var messageQueueTransaction = context.GetOrAdd(CurrentTransactionKey, () =>
            {
                var transaction = new MessageQueueTransaction();

                transaction.Begin();

                context.OnCommitted(async ctx => transaction.Commit());

                return transaction;
            });

            var sendQueues = context.GetOrAdd(CurrentOutgoingQueuesKey, () =>
            {
                var messageQueues = new ConcurrentDictionary<string, MessageQueue>(StringComparer.InvariantCultureIgnoreCase);

                context.OnDisposed(ctx =>
                {
                    foreach (var messageQueue in messageQueues.Values)
                    {
                        messageQueue.Dispose();
                    }
                });

                return messageQueues;
            });

            var path = MsmqUtil.GetFullPath(destinationAddress);

            var sendQueue = sendQueues.GetOrAdd(path, _ =>
            {
                var messageQueue = new MessageQueue(path, QueueAccessMode.Send);

                return messageQueue;
            });

            try
            {
                sendQueue.Send(logicalMessage, messageQueueTransaction);
            }
            catch (Exception exception)
            {
                throw new RebusApplicationException(exception, $"Could not send to MSMQ queue with path '{sendQueue.Path}'");
            }
        }

        /// <summary>
        /// Received the next available transport message from the input queue via MSMQ. Will create a new <see cref="MessageQueueTransaction"/> and stash
        /// it under the <see cref="CurrentTransactionKey"/> key in the given <paramref name="context"/>. If one already exists, an exception will be thrown
        /// (because we should never have to receive multiple messages in the same transaction)
        /// </summary>
        public async Task<TransportMessage> Receive(ITransactionContext context, CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (_inputQueueName == null)
            {
                throw new InvalidOperationException("This MSMQ transport does not have an input queue, hence it is not possible to reveive anything");
            }

            var queue = GetInputQueue();

            if (context.Items.ContainsKey(CurrentTransactionKey))
            {
                throw new InvalidOperationException("Tried to receive with an already existing MSMQ queue transaction - although it is possible with MSMQ to do so, with Rebus it is an indication that something is wrong!");
            }

            var messageQueueTransaction = new MessageQueueTransaction();
            messageQueueTransaction.Begin();

            context.OnDisposed(ctx => messageQueueTransaction.Dispose());
            context.Items[CurrentTransactionKey] = messageQueueTransaction;

            try
            {
                var message = queue.Receive(TimeSpan.FromSeconds(0.5), messageQueueTransaction);

                if (message == null)
                {
                    messageQueueTransaction.Abort();
                    return null;
                }

                context.OnCompleted(async ctx => messageQueueTransaction.Commit());
                context.OnDisposed(ctx => message.Dispose());

                var headers = _msmqHeaderSerializer.Deserialize(message) ?? new Dictionary<string, string>();
                var body = new byte[message.BodyStream.Length];

                await message.BodyStream.ReadAsync(body, 0, body.Length, cancellationToken);

                return new TransportMessage(headers, body);
            }
            catch (MessageQueueException exception)
            {
                if (exception.MessageQueueErrorCode == MessageQueueErrorCode.IOTimeout)
                {
                    return null;
                }

                if (exception.MessageQueueErrorCode == MessageQueueErrorCode.InvalidHandle)
                {
                    _log.Warn("Queue handle for {queueName} was invalid - will try to reinitialize the queue", _inputQueueName);
                    ReinitializeInputQueue();
                    return null;
                }

                if (exception.MessageQueueErrorCode == MessageQueueErrorCode.QueueDeleted)
                {
                    _log.Warn("Queue {queueName} was deleted - will not receive any more messages", _inputQueueName);
                    return null;
                }

                throw new IOException($"Could not receive next message from MSMQ queue '{_inputQueueName}'", exception);
            }
        }

        Message CreateMsmqMessage(TransportMessage message)
        {
            var headers = message.Headers;

            var expressDelivery = headers.ContainsKey(Headers.Express);
            var hasTimeout = headers.TryGetValue(Headers.TimeToBeReceived, out var timeToBeReceivedStr);

            var msmqMessage = new Message
            {
                BodyStream = new MemoryStream(message.Body),
                UseJournalQueue = false,
                Recoverable = !expressDelivery,
                UseDeadLetterQueue = !(expressDelivery || hasTimeout),
                Label = GetMessageLabel(message),
            };

            _msmqHeaderSerializer.SerializeToMessage(headers, msmqMessage);

            if (hasTimeout)
            {
                msmqMessage.TimeToBeReceived = TimeSpan.Parse(timeToBeReceivedStr);
            }

            return msmqMessage;
        }

        static string GetMessageLabel(TransportMessage message)
        {
            try
            {
                return message.GetMessageLabel();
            }
            catch
            {
                // if that failed, it's most likely because we're running in legacy mode - therefore:
                return message.Headers.GetValueOrNull(Headers.MessageId)
                       ?? message.Headers.GetValueOrNull("rebus-msg-id")
                       ?? "<unknown ID>";
            }
        }

        /// <summary>
        /// Gets the input queue address of this MSMQ queue
        /// </summary>
        public string Address => _inputQueueName;

        void ReinitializeInputQueue()
        {
            if (_inputQueue != null)
            {
                try
                {
                    _inputQueue.Close();
                    _inputQueue.Dispose();
                }
                catch (Exception exception)
                {
                    _log.Warn(exception, "An error occurred when closing/disposing the queue handle for {queueName}", _inputQueueName);
                }
                finally
                {
                    _inputQueue = null;
                }
            }

            GetInputQueue();

            _log.Info("Input queue handle successfully reinitialized");
        }

        MessageQueue GetInputQueue()
        {
            if (_inputQueue != null) return _inputQueue;

            lock (this)
            {
                if (_inputQueue != null) return _inputQueue;

                var inputQueuePath = MsmqUtil.GetPath(_inputQueueName);

                if (_newQueueCallbacks.Any())
                {
                    MsmqUtil.EnsureQueueExists(inputQueuePath, _log, messageQueue =>
                    {
                        _newQueueCallbacks.ForEach(callback => callback(messageQueue));
                    });
                }
                else
                {
                    MsmqUtil.EnsureQueueExists(inputQueuePath, _log);
                }
                MsmqUtil.EnsureMessageQueueIsTransactional(inputQueuePath);

                _inputQueue = new MessageQueue(inputQueuePath, QueueAccessMode.SendAndReceive)
                {
                    MessageReadPropertyFilter = new MessagePropertyFilter
                    {
                        Id = true,
                        Extension = true,
                        Body = true,
                    }
                };
            }

            return _inputQueue;
        }

        /// <summary>
        /// Disposes the input message queue object
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _inputQueue?.Dispose();
                _inputQueue = null;
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}