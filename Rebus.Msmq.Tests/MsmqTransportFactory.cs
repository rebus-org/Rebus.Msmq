using System;
using System.Collections.Generic;
using Rebus.Logging;
using Rebus.Tests.Contracts.Transports;
using Rebus.Transport;

namespace Rebus.Msmq.Tests
{
    public class MsmqTransportFactory : ITransportFactory
    {
        readonly List<IDisposable> _disposables = new List<IDisposable>();
        readonly HashSet<string> _queuesToDelete = new HashSet<string>();

        public ITransport CreateOneWayClient()
        {
            return Create(null);
        }

        public ITransport Create(string inputQueueAddress)
        {
            var transport = new MsmqTransport(inputQueueAddress, new ConsoleLoggerFactory(true), new ExtensionHeaderSerializer());

            _disposables.Add(transport);

            if (inputQueueAddress != null)
            {
                transport.PurgeInputQueue();
            }

            transport.Initialize();

            if (inputQueueAddress != null)
            {
                _queuesToDelete.Add(inputQueueAddress);
            }

            return transport;
        }

        public void CleanUp()
        {
            _disposables.ForEach(d => d.Dispose());
            _disposables.Clear();

            foreach (var queue in _queuesToDelete)
            {
                MsmqUtil.Delete(queue);
            }
            
            _queuesToDelete.Clear();
        }
    }
}