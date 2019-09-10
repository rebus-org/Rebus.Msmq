using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Tests.Contracts;
using Rebus.Transport;

namespace Rebus.Msmq.Tests
{
    [TestFixture]
    public class TestMsmqUtil : FixtureBase
    {
        const string QueueName = "some-randomly-named-queue";

        protected override void SetUp()
        {
            MsmqUtil.Delete(QueueName);
        }

        protected override void TearDown()
        {
            MsmqUtil.Delete(QueueName);
        }

        [Test]
        public async Task CanGetCount()
        {
            var path = MsmqUtil.GetPath(QueueName);

            MsmqUtil.EnsureQueueExists(path);

            Console.WriteLine($"Checking {path}");

            var countBefore = MsmqUtil.GetCount(path);

            await SendMessageTo(QueueName);
            await SendMessageTo(QueueName);
            await SendMessageTo(QueueName);

            var countAfter = MsmqUtil.GetCount(path);

            Assert.That(countBefore, Is.EqualTo(0));
            Assert.That(countAfter, Is.EqualTo(3));
        }

        static async Task SendMessageTo(string queueName)
        {
            using (var transport = new MsmqTransport(queueName, new ConsoleLoggerFactory(false), new ExtensionHeaderSerializer()))
            {
                using (var scope = new RebusTransactionScope())
                {
                    var transportMessage = new TransportMessage(new Dictionary<string, string>(), new byte[0]);

                    await transport.Send(queueName, transportMessage, scope.TransactionContext);

                    await scope.CompleteAsync();
                }
            }
        }
    }
}