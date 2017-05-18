using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Transport;
#pragma warning disable 1998

namespace Rebus.Msmq
{
    class MsmqTransportInspector : ITransportInspector
    {
        readonly string _queueName;

        public MsmqTransportInspector(string queueName)
        {
            _queueName = queueName;
        }

        public async Task<Dictionary<string, object>> GetProperties(CancellationToken cancellationToken)
        {
            return new Dictionary<string, object>
            {
                {TransportInspectorPropertyKeys.QueueLength, GetCount().ToString()}
            };
        }

        int GetCount()
        {
            var path = MsmqUtil.GetPath(_queueName);

            return MsmqUtil.GetCount(path);
        }
    }
}