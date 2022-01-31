﻿using System;
using System.Linq;
using System.Messaging;
using System.Security.Principal;
using System.Threading;
using Rebus.Exceptions;
using Rebus.Logging;

namespace Rebus.Msmq
{
    /// <summary>
    /// Utils class for various MSMQ operations
    /// </summary>
    public static class MsmqUtil
    {
        /// <summary>
        /// Deletes all messages from the queue with the specified name
        /// </summary>
        /// <param name="queueName"></param>
        public static void PurgeQueue(string queueName)
        {
            var path = GetPath(queueName);

            if (!MessageQueue.Exists(path)) return;

            using (var messageQueue = new MessageQueue(path))
            {
                messageQueue.Purge();
            }
        }

        /// <summary>
        /// Gets the MSMQ style path for the queue with the given name
        /// </summary>
        public static string GetPath(string queueName)
        {
            var bim = Parse(queueName);

            return GenerateSimplePath(bim.MachineName ?? ".", bim.QueueName);
        }

        /// <summary>
        /// Gets the MSMQ style path for the queue with the given name, always machine-qualified, even when the queue is local
        /// </summary>
        public static string GetFullPath(string queueName)
        {
            var bim = Parse(queueName);

            return GenerateFullPath(bim.MachineName ?? Environment.MachineName, bim.QueueName);
        }

        /// <summary>
        /// Generates from the given <paramref name="machineName"/> and <paramref name="queueName"/> the full, MSMQ style queue path.
        /// It goes something like this: <code>FormatName:DIRECT=OS:SOME_MACHINE\private$\SOME_QUEUE</code> when addressing
        /// by machine name, and like this <code>FormatName:DIRECT=TCP:10.10.1.1\private$\SOME_QUEUE</code> when addressing
        /// by IP address.
        /// </summary>
        public static string GenerateFullPath(string machineName, string queueName)
        {
            if (IsIpAddress(machineName))
            {
                return $@"FormatName:DIRECT=TCP:{machineName.ToLower()}\private$\{queueName}";
            }

            return $@"FormatName:DIRECT=OS:{machineName.ToLower()}\private$\{queueName}";
        }

        static string GenerateSimplePath(string machineName, string queueName)
        {
            return $@"{machineName}\private$\{queueName}";
        }

        static bool IsIpAddress(string machineName)
        {
            var ipTokens = machineName.Split('.');

            return ipTokens.Length == 4 && ipTokens.All(IsByte);
        }

        static bool IsByte(string str)
        {
            return byte.TryParse(str, out _);
        }

        class QueueInfo
        {
            public string MachineName { get; set; }
            public string QueueName { get; set; }
        }

        /// <summary>
        /// Returns whether an MSMQ queue with the given name exists
        /// </summary>
        public static bool QueueExists(string queueName)
        {
            return MessageQueue.Exists(GetPath(queueName));
        }

        /// <summary>
        /// Deletes the MSMQ queue with the given name
        /// </summary>
        public static void Delete(string queueName)
        {
            var queuePath = GetPath(queueName);
            if (!MessageQueue.Exists(queuePath)) return;

            MessageQueue.Delete(queuePath);
        }

        static QueueInfo Parse(string queueName)
        {
            if (queueName.Contains("@"))
            {
                var tokens = queueName.Split('@');

                if (tokens.Length != 2)
                {
                    throw new ArgumentException($@"The specified MSMQ queue name is invalid!: {queueName} - please format queue names according to one of the following examples:

    someQueue

in order to specify a private queue named 'someQueue' on the local machine, or

    someQueue@anotherMachine

in order to specify a private queue named 'someQueue' on the machine with hostname 'anotherMachine', or

    someQueue@10.0.1.45

in order to specify a private queue named 'someQueue' on the machine with IP 10.0.1.45");
                }
                return new QueueInfo { QueueName = tokens[0], MachineName = tokens[1] };
            }
            return new QueueInfo { QueueName = queueName };
        }

        /// <summary>
        /// Creates the MSMQ queue with the specified path if it does not already exist. If it is created, the user account
        /// of the currently executing process will get <see cref="MessageQueueAccessRights.GenericWrite"/> permissions to it,
        /// and the local administrators group will get <see cref="MessageQueueAccessRights.FullControl"/>.
        /// </summary>
        public static void EnsureQueueExists(string inputQueuePath, ILog log = null, Action<MessageQueue> permissionsCallback = null)
        {
            if (MessageQueue.Exists(inputQueuePath)) return;

            try
            {
                log?.Info("Queue {queueName} does not exist - it will be created now", inputQueuePath);

                var newQueue = MessageQueue.Create(inputQueuePath, true);
                var administratorAccountName = GetAdministratorsGroupAccountName();

                newQueue.SetPermissions(Thread.CurrentPrincipal.Identity.Name, MessageQueueAccessRights.GenericWrite);
                newQueue.SetPermissions(administratorAccountName, MessageQueueAccessRights.FullControl);

                if (permissionsCallback != null)
                {
                    log?.Info("Custom MessageQueue callback will be invoked");
                    permissionsCallback.Invoke(newQueue);
                }
            }
            catch (MessageQueueException exception)
            {
                if (exception.MessageQueueErrorCode == MessageQueueErrorCode.QueueExists)
                {
                    // all is good ;)
                    return;
                }

                throw;
            }
        }

        static string GetAdministratorsGroupAccountName()
        {
            try
            {
                return new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
                    .Translate(typeof(NTAccount))
                    .ToString();
            }
            catch (Exception exception)
            {
                throw new RebusApplicationException(exception, "An error occurred while attempting to figure out the name of the local administrators group!");
            }
        }

        /// <summary>
        /// Verifies that the queue with the given <paramref name="path"/> is transaction. If that is not the case, a <see cref="RebusApplicationException"/>
        /// is thrown with an explanation of the problem
        /// </summary>
        public static void EnsureMessageQueueIsTransactional(string path)
        {
            using (var queue = new MessageQueue(path))
            {
                if (queue.Transactional) return;

                var message = $@"The queue {path} is NOT transactional!

Everything around Rebus is built with the assumption that queues are transactional, so Rebus will malfunction if queues aren't transactional. 

To remedy this, ensure that any existing queues are transactional, or let Rebus create its queues automatically.

If Rebus allowed you to work with non-transactional queues, it would not be able to e.g. safely move a received message into an error queue. Also, MSMQ does not behave well when moving messages in/out of transactional/non-transactional queues - the messages will end up in the dead-letter queue.";

                throw new RebusApplicationException(message);
            }
        }

        /// <summary>
        /// Returns whther the queue with the specified name is local
        /// </summary>
        public static bool IsLocal(string queueName)
        {
            var queueInfo = Parse(queueName);

            return queueInfo.MachineName == null
                   || queueInfo.MachineName == "."
                   || queueInfo.MachineName.Equals(Environment.MachineName, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        /// Gets the number of messages from the queue with the given <paramref name="path"/>
        /// </summary>
        public static int GetCount(string path)
        {
            using (var queue = new MessageQueue(path))
            {
                return GetCount(queue);
            }

            //return (int)MessageQueueExtensions.GetCount(path);
        }

        static int GetCount(MessageQueue queue)
        {
            try
            {
                var count = 0;

                using (var enumerator = queue.GetMessageEnumerator2())
                {
                    while (enumerator.MoveNext()) count++;

                    return count;
                }
            }
            catch
            {
                return 0;
            }
        }
    }
}