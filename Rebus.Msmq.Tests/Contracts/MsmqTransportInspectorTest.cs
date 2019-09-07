using NUnit.Framework;
using Rebus.Tests.Contracts.Transports;

namespace Rebus.Msmq.Tests.Contracts
{
    [TestFixture]
    public class MsmqTransportInspectorTest : TransportInspectorTest<MsmqTransportInspectorFactory>
    {
    }
}