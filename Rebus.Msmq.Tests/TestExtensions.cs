using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Rebus.Messages;
using Rebus.Transport;

namespace Rebus.Msmq.Tests;

static class TestExtensions
{
    public static async Task<IReadOnlyList<TransportMessage>> ReceiveAllAsync(this MsmqTransport transport)
    {
        if (transport == null) throw new ArgumentNullException(nameof(transport));

        var messages = new List<TransportMessage>();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = timeout.Token;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var scope = new RebusTransactionScope();
            var message = await transport.Receive(scope.TransactionContext, cancellationToken);
            await scope.CompleteAsync();

            if (message == null) return messages;
            
            messages.Add(message);
        }
    }
}