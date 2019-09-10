﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Tests.Contracts;
using Rebus.Transport;

namespace Rebus.Msmq.Tests
{
    [TestFixture]
    public class TestMsmqTransportMachineAddressing : FixtureBase
    {
        readonly string _queueName = TestConfig.GetName("input");
        MsmqTransport _transport;
        CancellationToken _cancellationToken;

        protected override void SetUp()
        {
            _transport = new MsmqTransport(_queueName, new ConsoleLoggerFactory(true), new ExtensionHeaderSerializer());
            _transport.CreateQueue(_queueName);

            Using(_transport);

            Console.WriteLine(_queueName);

            _cancellationToken = new CancellationTokenSource().Token;
        }

        protected override void TearDown()
        {
            MsmqUtil.Delete(_queueName);
        }

        [Test]
        public void CanDoOrdinarySend()
        {
            var destinationAddress = _queueName;

            Send(destinationAddress, "hej");

            var msg = Receive();

            Assert.That(msg, Is.EqualTo("hej"));
        }

        [Test]
        public void CanDoSendToOwnMachineName()
        {
            var destinationAddress = $"{_queueName}@{Environment.MachineName}";

            Send(destinationAddress, "hej");

            var msg = Receive();

            Assert.That(msg, Is.EqualTo("hej"));
        }

        [Test]
        public void CanDoSendToLocalhost()
        {
            var destinationAddress = $"{_queueName}@localhost";

            Send(destinationAddress, "hej");

            var msg = Receive();

            Assert.That(msg, Is.EqualTo("hej"));
        }

        [Test]
        public void CanDoSendToDot()
        {
            var destinationAddress = $"{_queueName}@.";

            Send(destinationAddress, "hej");

            var msg = Receive();

            Assert.That(msg, Is.EqualTo("hej"));
        }

        [Test]
        public void CanDoSendToOwnIpAddress()
        {
            var ownFirstIpv4Address = Dns.GetHostAddresses(Environment.MachineName)
                .First(a => a.AddressFamily == AddressFamily.InterNetwork);

            var destinationAddress = $"{_queueName}@{ownFirstIpv4Address.MapToIPv4()}";

            Send(destinationAddress, "hej");

            var msg = Receive();

            Assert.That(msg, Is.EqualTo("hej"));
        }

        string Receive()
        {
            using (var scope = new RebusTransactionScope())
            {
                var transportMessage = _transport.Receive(scope.TransactionContext, _cancellationToken).Result;

                scope.Complete();

                if (transportMessage == null) return null;

                return Encoding.UTF8.GetString(transportMessage.Body);
            }

        }

        void Send(string destinationAddress, string message)
        {
            Console.WriteLine("Sending to {0}", destinationAddress);

            using (var scope = new RebusTransactionScope())
            {
                _transport.Send(destinationAddress, NewMessage(message), scope.TransactionContext).Wait();
                scope.Complete();
            }
        }

        TransportMessage NewMessage(string contents)
        {
            var headers = new Dictionary<string, string>
            {
                {Headers.MessageId, Guid.NewGuid().ToString()}
            };

            return new TransportMessage(headers, Encoding.UTF8.GetBytes(contents));
        }
    }
}