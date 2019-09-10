using System;
using System.Collections.Generic;
using System.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Messages;
using Rebus.Serialization;
using Rebus.Tests.Contracts;
using Message = System.Messaging.Message;

namespace Rebus.Msmq.Tests
{
    [TestFixture]
    [Description("Verify that serializer can ignore mangled message")]
    public class TestMsmqIgnoreMangledMessage : FixtureBase
    {
        readonly string _inputQueueName = TestConfig.GetName("handled-mangled-message");

        protected override void SetUp()
        {
            MsmqUtil.Delete(_inputQueueName);

            Configure.With(Using(new BuiltinHandlerActivator()))
                .Transport(t => t.UseMsmq(_inputQueueName))
                .Options(opt =>
                {
                    opt.Decorate<IMsmqHeaderSerializer>(p => new MangledMessageIngoreSerializer());
                })
                .Serialization(c => c.Register(_ => new StaticMessageDeserializer()))
                .Start();
        }

        [Test]
        public void MangledMessageIsReceived()
        {
            using (var messageQueue = new MessageQueue(MsmqUtil.GetPath(_inputQueueName)))
            {
                var transaction = new MessageQueueTransaction();
                transaction.Begin();
                messageQueue.Send(new Message
                {
                    Extension = Encoding.UTF32.GetBytes("this is definitely not valid UTF8-encoded JSON")
                }, transaction);
                transaction.Commit();
            }

            Thread.Sleep(5000);

            CleanUpDisposables();

            using (var messageQueue = new MessageQueue(MsmqUtil.GetPath(_inputQueueName)))
            {
                messageQueue.MessageReadPropertyFilter = new MessagePropertyFilter
                {
                    Extension = true
                };

                Assert.Catch<MessageQueueException>(() => messageQueue.Receive(TimeSpan.FromSeconds(2)));
            }
        }
    }

    public class StaticMessageDeserializer : ISerializer
    {
        public Task<TransportMessage> Serialize(Messages.Message message)
        {
            throw new NotImplementedException();
        }

        public Task<Rebus.Messages.Message> Deserialize(TransportMessage transportMessage)
        {
            var msg = new Messages.Message(transportMessage.Headers, "test");
            return Task.FromResult(msg);
        }
    }

    public class MangledMessageIngoreSerializer : IMsmqHeaderSerializer
    {
        public void SerializeToMessage(Dictionary<string, string> headers, Message msmqMessage)
        {
        }

        public Dictionary<string, string> Deserialize(Message msmqMessage)
        {
            return new Dictionary<string, string>() { { Headers.MessageId, msmqMessage.Id } };
        }
    }
}