using Rebus.Logging;
using Rebus.Tests.Contracts.Transports;

namespace Rebus.Msmq.Tests.Contracts
{
    public class MsmqTransportInspectorFactory : ITransportInspectorFactory
    {
        public TransportAndInspector Create(string address)
        {
            var transport = new MsmqTransport(address, new ConsoleLoggerFactory(false), new ExtensionHeaderSerializer());
            var transportInspector = new MsmqTransportInspector(address);

            return new TransportAndInspector(transport, transportInspector);
        }

        public void Dispose()
        {
        }
    }
}