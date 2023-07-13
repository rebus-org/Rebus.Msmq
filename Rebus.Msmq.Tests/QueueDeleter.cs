using System;

namespace Rebus.Msmq.Tests;

class QueueDeleter : IDisposable
{
    readonly string _queueName;

    public QueueDeleter(string queueName) => _queueName = queueName ?? throw new ArgumentNullException(nameof(queueName));

    public void Dispose() => MsmqUtil.Delete(_queueName);
}