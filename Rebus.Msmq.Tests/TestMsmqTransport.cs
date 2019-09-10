﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Messaging;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using NUnit.Framework;
using Rebus.Extensions;
using Rebus.Logging;
using Rebus.Tests.Contracts;
using Rebus.Tests.Contracts.Extensions;
using Rebus.Transport;
using Timer = System.Timers.Timer;

namespace Rebus.Msmq.Tests
{
    [TestFixture]
    [Ignore("not an actual test")]
    public class TestMsmqTransport : FixtureBase
    {
        static readonly string QueueName = TestConfig.GetName("test.performance");

        [TestCase(100)]
        public void HowFast(int messageCount)
        {
            var stopwatch = Stopwatch.StartNew();

            SendMessages(messageCount);

            var totalSeconds = stopwatch.Elapsed.TotalSeconds;

            Console.WriteLine($"Sending {messageCount} msgs took {totalSeconds:0.0} s - that's {messageCount/totalSeconds:0.0} msg/s");
        }

        static void PrintConclusion(int messageCount, Stopwatch stopwatch)
        {
            var totalSeconds = stopwatch.Elapsed.TotalSeconds;

            Console.WriteLine($"{messageCount} messages received in {totalSeconds:0.0} s - that's {messageCount/totalSeconds:0.0} msg/s");
        }

        [TestCase(10, 1)]
        [TestCase(1000, 1)]
        [TestCase(1000, 2)]
        [TestCase(1000, 5)]
        [TestCase(1000, 10)]
        [TestCase(1000, 20)]
        [TestCase(10000, 1)]
        [TestCase(10000, 2)]
        [TestCase(10000, 5)]
        [TestCase(10000, 10)]
        [TestCase(10000, 20)]
        public void ReceivePerformanceWithThreadsOnMultipleInstances(int messageCount, int concurrencyLevel)
        {
            var allMessagesReceived = new ManualResetEvent(false);
            var fileName = Path.Combine(@"C:\temp", $"msmqtest-threaded-multi-{messageCount}-{concurrencyLevel}.txt");

            File.WriteAllText(fileName, "Go!" + Environment.NewLine);

            var stopwatch = Stopwatch.StartNew();
            var sentIds = SendMessages(messageCount).ToConcurrentDictionary(v => v);
            var counter = 0;
            var keepRunning = true;
            var stuffToDispose = new List<IDisposable>();

            using (var timer = new Timer(1000))
            {
                var queues = Enumerable.Range(0, concurrencyLevel)
                    .Select(i =>
                    {
                        var queue = GetMessageQueue(QueueName);
                        stuffToDispose.Add(queue);

                        var thread = new Thread(() =>
                        {
                            while (keepRunning)
                            {
                                using (var messageQueueTransaction = new MessageQueueTransaction())
                                {
                                    messageQueueTransaction.Begin();
                                    try
                                    {
                                        var msmqMessage = queue.Receive(TimeSpan.FromSeconds(1), messageQueueTransaction);
                                        if (msmqMessage == null)
                                        {
                                            Thread.Sleep(100);
                                            continue;
                                        }
                                        using (var reader = new StreamReader(msmqMessage.BodyStream, Encoding.UTF8))
                                        {
                                            var message = JsonConvert.DeserializeObject<SomeMessage>(reader.ReadToEnd());

                                            int bimse;
                                            sentIds.TryRemove(message.Id, out bimse);

                                            Interlocked.Increment(ref counter);

                                            if (sentIds.Count == 0)
                                            {
                                                allMessagesReceived.Set();
                                            }

                                            messageQueueTransaction.Commit();
                                        }
                                    }
                                    catch (MessageQueueException ex)
                                    {
                                        if (ex.MessageQueueErrorCode != MessageQueueErrorCode.IOTimeout)
                                        {
                                            Console.WriteLine(ex);
                                        }

                                        messageQueueTransaction.Abort();
                                    }
                                }
                            }
                        });

                        thread.Start();

                        return thread;
                    })
                    .ToList();

                timer.Elapsed += delegate
                {
                    AppendStatus(counter, stopwatch, fileName);
                };

                timer.Start();

                allMessagesReceived.WaitOne();

                PrintConclusion(messageCount, stopwatch);

                keepRunning = false;

                AppendStatus(counter, stopwatch, fileName);

                queues.ForEach(q => q.Join());
                stuffToDispose.ForEach(d => d.Dispose());
            }
        }

        [TestCase(10, 1)]
        [TestCase(1000, 1)]
        [TestCase(1000, 2)]
        [TestCase(1000, 5)]
        [TestCase(1000, 10)]
        [TestCase(1000, 20)]
        [TestCase(10000, 1)]
        [TestCase(10000, 2)]
        [TestCase(10000, 5)]
        [TestCase(10000, 10)]
        [TestCase(10000, 20)]
        public void ReceivePerformanceWithThreadsOnSingleInstance(int messageCount, int concurrencyLevel)
        {
            var allMessagesReceived = new ManualResetEvent(false);
            var fileName = Path.Combine(@"C:\temp", $"msmqtest-threaded-single-{messageCount}-{concurrencyLevel}.txt");

            File.WriteAllText(fileName, "Go!" + Environment.NewLine);

            var stopwatch = Stopwatch.StartNew();
            var sentIds = SendMessages(messageCount).ToConcurrentDictionary(v => v);
            var keepRunning = true;
            var stuffToDispose = new List<IDisposable>();

            using (var timer = new Timer(1000))
            {
                var queue = GetMessageQueue(QueueName);
                stuffToDispose.Add(queue);

                var queues = Enumerable.Range(0, concurrencyLevel)
                    .Select(i =>
                    {
                        var thread = new Thread(() =>
                        {
                            while (keepRunning)
                            {
                                using (var messageQueueTransaction = new MessageQueueTransaction())
                                {
                                    messageQueueTransaction.Begin();
                                    try
                                    {
                                        var msmqMessage = queue.Receive(TimeSpan.FromSeconds(1), messageQueueTransaction);
                                        if (msmqMessage == null)
                                        {
                                            Thread.Sleep(100);
                                            continue;
                                        }
                                        using (var reader = new StreamReader(msmqMessage.BodyStream, Encoding.UTF8))
                                        {
                                            var message = JsonConvert.DeserializeObject<SomeMessage>(reader.ReadToEnd());

                                            int bimse;
                                            sentIds.TryRemove(message.Id, out bimse);

                                            if (sentIds.Count == 0)
                                            {
                                                allMessagesReceived.Set();
                                            }

                                            messageQueueTransaction.Commit();
                                        }
                                    }
                                    catch (MessageQueueException ex)
                                    {
                                        if (ex.MessageQueueErrorCode != MessageQueueErrorCode.IOTimeout)
                                        {
                                            Console.WriteLine(ex);
                                        }

                                        messageQueueTransaction.Abort();
                                    }
                                }
                            }
                        });

                        thread.Start();

                        return thread;
                    })
                    .ToList();

                timer.Elapsed += delegate
                {
                    AppendStatus(messageCount - sentIds.Count, stopwatch, fileName);
                };

                timer.Start();

                allMessagesReceived.WaitOne();

                PrintConclusion(messageCount, stopwatch);

                keepRunning = false;

                AppendStatus(messageCount - sentIds.Count, stopwatch, fileName);

                queues.ForEach(q => q.Join());
                stuffToDispose.ForEach(d => d.Dispose());
            }
        }

        [TestCase(1000, 1)]
        [TestCase(1000, 2)]
        [TestCase(1000, 5)]
        [TestCase(1000, 10)]
        [TestCase(1000, 20)]
        [TestCase(10000, 1)]
        [TestCase(10000, 2)]
        [TestCase(10000, 5)]
        [TestCase(10000, 10)]
        [TestCase(10000, 20)]
        public void ReceivePerformanceWithEventOnMultipleInstances(int messageCount, int concurrencyLevel)
        {
            var allMessagesReceived = new ManualResetEvent(false);
            var fileName = Path.Combine(@"C:\temp", $"msmqtest-multi-{messageCount}-{concurrencyLevel}.txt");

            File.WriteAllText(fileName, "Go!" + Environment.NewLine);

            var stopwatch = Stopwatch.StartNew();
            var sentIds = SendMessages(messageCount).ToConcurrentDictionary(v => v);
            var counter = 0;

            using (var timer = new Timer(1000))
            {
                var queues = Enumerable.Range(0, concurrencyLevel)
                    .Select(i =>
                    {
                        var queue = GetMessageQueue(QueueName);

                        queue.ReceiveCompleted += (o, ea) =>
                        {
                            using (var reader = new StreamReader(ea.Message.BodyStream, Encoding.UTF8))
                            {
                                var message = JsonConvert.DeserializeObject<SomeMessage>(reader.ReadToEnd());

                                int bimse;
                                sentIds.TryRemove(message.Id, out bimse);

                                Interlocked.Increment(ref counter);

                                if (sentIds.Count == 0)
                                {
                                    allMessagesReceived.Set();
                                }

                                queue.BeginReceive();
                            }
                        };

                        return queue;
                    })
                    .ToList();

                timer.Elapsed += delegate
                {
                    AppendStatus(counter, stopwatch, fileName);
                };

                timer.Start();

                queues.ForEach(q => q.BeginReceive());

                allMessagesReceived.WaitOne();

                PrintConclusion(messageCount, stopwatch);

                AppendStatus(counter, stopwatch, fileName);

                queues.ForEach(q => q.Dispose());
            }
        }

        [TestCase(1000, 1)]
        [TestCase(1000, 2)]
        [TestCase(1000, 5)]
        [TestCase(1000, 10)]
        [TestCase(1000, 20)]
        [TestCase(10000, 1)]
        [TestCase(10000, 2)]
        [TestCase(10000, 5)]
        [TestCase(10000, 10)]
        [TestCase(10000, 20)]
        public void ReceivePerformanceWithEventOnSingleInstance(int messageCount, int concurrencyLevel)
        {
            var allMessagesReceived = new ManualResetEvent(false);
            var fileName = Path.Combine(@"C:\temp", $"msmqtest-single-{messageCount}-{concurrencyLevel}.txt");

            File.WriteAllText(fileName, "Go!" + Environment.NewLine);

            var stopwatch = Stopwatch.StartNew();
            var sentIds = SendMessages(messageCount).ToConcurrentDictionary(v => v);

            using (var timer = new Timer(1000))
            using (var queue = GetMessageQueue(QueueName))
            {
                var counter = 0;

                timer.Elapsed += delegate
                {
                    AppendStatus(counter, stopwatch, fileName);
                };

                timer.Start();

                queue.ReceiveCompleted += (o, ea) =>
                {
                    using (var reader = new StreamReader(ea.Message.BodyStream, Encoding.UTF8))
                    {
                        var message = JsonConvert.DeserializeObject<SomeMessage>(reader.ReadToEnd());

                        int bimse;
                        sentIds.TryRemove(message.Id, out bimse);

                        Interlocked.Increment(ref counter);

                        if (sentIds.Count == 0)
                        {
                            allMessagesReceived.Set();
                        }

                        queue.BeginReceive();
                    }
                };

                concurrencyLevel.Times(() => queue.BeginReceive());

                allMessagesReceived.WaitOne();

                PrintConclusion(messageCount, stopwatch);

                AppendStatus(counter, stopwatch, fileName);
            }
        }

        [TestCase(1000, 1)]
        [TestCase(1000, 2)]
        [TestCase(1000, 5)]
        [TestCase(1000, 10)]
        [TestCase(1000, 20)]
        [TestCase(10000, 1)]
        [TestCase(10000, 2)]
        [TestCase(10000, 5)]
        [TestCase(10000, 10)]
        [TestCase(10000, 20)]
        public void ReceivePerformanceWithEventAndPeeking(int messageCount, int concurrencyLevel)
        {
            var allMessagesReceived = new ManualResetEvent(false);
            var fileName = Path.Combine(@"C:\temp", $"msmqtest-peek-single-{messageCount}-{concurrencyLevel}.txt");

            File.WriteAllText(fileName, "Go!" + Environment.NewLine);

            var stopwatch = Stopwatch.StartNew();
            var sentIds = SendMessages(messageCount).ToConcurrentDictionary(v => v);

            using (var timer = new Timer(1000))
            using (var queue = GetMessageQueue(QueueName))
            {
                var counter = 0;

                timer.Elapsed += delegate
                {
                    AppendStatus(counter, stopwatch, fileName);
                };

                timer.Start();

                queue.PeekCompleted += (o, ea) =>
                {
                    using (var transaction = new MessageQueueTransaction())
                    {
                        try
                        {
                            transaction.Begin();

                            var trannyMessage = queue.Receive(TimeSpan.FromSeconds(0.1));

                            using (var reader = new StreamReader(trannyMessage.BodyStream, Encoding.UTF8))
                            {
                                var message = JsonConvert.DeserializeObject<SomeMessage>(reader.ReadToEnd());

                                int bimse;
                                sentIds.TryRemove(message.Id, out bimse);

                                Interlocked.Increment(ref counter);

                                if (sentIds.Count == 0)
                                {
                                    allMessagesReceived.Set();
                                }

                                queue.BeginPeek();
                            }

                            transaction.Commit();
                        }
                        catch (Exception)
                        {
                            transaction.Abort();
                        }
                    }
                };


                concurrencyLevel.Times(() => queue.BeginPeek());

                allMessagesReceived.WaitOne();

                PrintConclusion(messageCount, stopwatch);

                AppendStatus(counter, stopwatch, fileName);
            }
        }

        static MessageQueue GetMessageQueue(string queueName)
        {
            var queue = new MessageQueue(MsmqUtil.GetPath(queueName));
            queue.MessageReadPropertyFilter = new MessagePropertyFilter
            {
                Id = true,
                Extension = true,
                Body = true,
            };
            return queue;
        }

        static void AppendStatus(int counter, Stopwatch stopwatch, string fileName)
        {
            var text = $"{counter} msgs received ({counter/stopwatch.Elapsed.TotalSeconds:0.0} msg/s)";

            File.AppendAllText(fileName, text + Environment.NewLine);
        }

        static List<int> SendMessages(int messageCount)
        {
            var transport = new MsmqTransport(QueueName, new ConsoleLoggerFactory(true), new ExtensionHeaderSerializer());

            MsmqUtil.EnsureQueueExists(MsmqUtil.GetPath(QueueName));

            var sendIds = new List<int>();

            Enumerable.Range(0, messageCount)
                .Select(id => new SomeMessage { Id = id })
                .ToList()
                .ForEach(msg =>
                {
                    using (var scope = new RebusTransactionScope())
                    {
                        transport.Send(QueueName, TransportMessageHelpers.FromString(JsonConvert.SerializeObject(msg)), scope.TransactionContext).Wait();

                        scope.Complete();

                        sendIds.Add(msg.Id);
                    }
                });

            return sendIds;
        }

        class SomeMessage
        {
            public int Id { get; set; }

            public override string ToString()
            {
                return $"msg {Id}";
            }
        }
    }
}