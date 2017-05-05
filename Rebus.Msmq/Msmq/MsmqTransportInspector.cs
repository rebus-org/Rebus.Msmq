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

        public async Task<int> GetCount(CancellationToken cancellationToken)
        {
            var path = MsmqUtil.GetPath(_queueName);

            return MsmqUtil.GetCount(path);
        }
    }
}