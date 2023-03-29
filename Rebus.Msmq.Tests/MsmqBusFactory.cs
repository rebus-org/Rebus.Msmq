﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Tests.Contracts;
using Rebus.Tests.Contracts.Transports;

namespace Rebus.Msmq.Tests;

public class MsmqBusFactory : IBusFactory
{
    readonly List<IDisposable> _stuffToDispose = new List<IDisposable>();

    public IBus GetBus<TMessage>(string inputQueueAddress, Func<TMessage, Task> handler)
    {
        var queueName = TestConfig.GetName(inputQueueAddress);
        MsmqUtil.Delete(queueName);

        var builtinHandlerActivator = new BuiltinHandlerActivator();

        builtinHandlerActivator.Handle(handler);

        var bus = Configure.With(builtinHandlerActivator)
            .Transport(t => t.UseMsmq(queueName))
            .Options(o => o.SetNumberOfWorkers(10))
            .Start();

        _stuffToDispose.Add(bus);

        return bus;
    }

    public void Cleanup()
    {
        _stuffToDispose.ForEach(d => d.Dispose());
        _stuffToDispose.Clear();
    }
}