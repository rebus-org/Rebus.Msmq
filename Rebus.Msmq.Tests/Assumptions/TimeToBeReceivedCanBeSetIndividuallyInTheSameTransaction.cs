using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Tests.Contracts;
using Rebus.Transport;

namespace Rebus.Msmq.Tests.Assumptions;

[TestFixture]
[Explicit("Explicit because the tests fail. Looks like a bug (or a limitation) in MSMQ")]
public class TimeToBeReceivedCanBeSetIndividuallyInTheSameTransaction : FixtureBase
{
    [Test]
    public async Task ComeOn_FirstMessageWithoutTtl()
    {
        var queueName = Guid.NewGuid().ToString("N");
        Using(new QueueDeleter(queueName));
        var transport = new MsmqTransport(queueName, new ConsoleLoggerFactory(colored: false), new ExtensionHeaderSerializer());
        Using(transport);
        transport.Initialize();

        using var scope = new RebusTransactionScope();
        await transport.Send(queueName, new(CreateValidHeaders(), new byte[] { 1, 2, 3 }), scope.TransactionContext);
        await transport.Send(queueName, new(CreateValidHeadersWithTtl(), new byte[] { 1, 2, 3, 4, 5, 6 }), scope.TransactionContext);
        await scope.CompleteAsync();

        await Task.Delay(TimeSpan.FromSeconds(6));
        var messages = await transport.ReceiveAllAsync();
        Assert.That(messages.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task ComeOn_FirstMessageWithTtl()
    {
        var queueName = Guid.NewGuid().ToString("N");
        Using(new QueueDeleter(queueName));
        var transport = new MsmqTransport(queueName, new ConsoleLoggerFactory(colored: false), new ExtensionHeaderSerializer());
        Using(transport);
        transport.Initialize();

        using var scope = new RebusTransactionScope();
        await transport.Send(queueName, new(CreateValidHeadersWithTtl(), new byte[] { 1, 2, 3, 4, 5, 6 }), scope.TransactionContext);
        await transport.Send(queueName, new(CreateValidHeaders(), new byte[] { 1, 2, 3 }), scope.TransactionContext);
        await scope.CompleteAsync();

        await Task.Delay(TimeSpan.FromSeconds(6));
        var messages = await transport.ReceiveAllAsync();
        Assert.That(messages.Count, Is.EqualTo(1));
    }


    Dictionary<string, string> CreateValidHeaders() => new() { [Headers.MessageId] = Guid.NewGuid().ToString("N") };

    Dictionary<string, string> CreateValidHeadersWithTtl() => new()
    {
        [Headers.MessageId] = Guid.NewGuid().ToString("N"),
        [Headers.TimeToBeReceived] = "00:00:05"
    };
}