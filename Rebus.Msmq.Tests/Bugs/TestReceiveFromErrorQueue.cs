using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rebus.Logging;
using Rebus.Tests.Contracts;
using Rebus.Transport;

namespace Rebus.Msmq.Tests.Bugs
{
    [TestFixture]
    public class TestReceiveFromErrorQueue : FixtureBase
    {
        [Test]
        public async Task CanReceiveFromEmptyErrorQueueWithoutGettingExceptions()
        {
            var transport = new MsmqTransport("error", new ConsoleLoggerFactory(false), new ExtensionHeaderSerializer());

            Using(transport);

            transport.PurgeInputQueue();

            using (var scope = new RebusTransactionScope())
            {
                var transportMessage = await transport.Receive(scope.TransactionContext, CancellationToken.None);

                Assert.That(transportMessage, Is.Null);
            }
        }
    }
}