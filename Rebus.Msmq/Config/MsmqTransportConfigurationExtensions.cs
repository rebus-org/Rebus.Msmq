using System;
using Rebus.Logging;
using Rebus.Msmq;
using Rebus.Transport;
// ReSharper disable UnusedMember.Global

namespace Rebus.Config
{
    /// <summary>
    /// Configuration extensions for the MSMQ transport
    /// </summary>
    public static class MsmqTransportConfigurationExtensions
    {
        /// <summary>
        /// Configures Rebus to use MSMQ to transport messages, receiving messages from the specified <paramref name="inputQueueName"/>
        /// </summary>
        public static MsmqTransportConfigurationBuilder UseMsmq(this StandardConfigurer<ITransport> configurer, string inputQueueName)
        {
            if (configurer == null) throw new ArgumentNullException(nameof(configurer));
            if (inputQueueName == null) throw new ArgumentNullException(nameof(inputQueueName));

            var builder = new MsmqTransportConfigurationBuilder();

            configurer.Register(c =>
            {
                var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                var extensionDeserializer = c.Has<IMsmqHeaderSerializer>(false) ? c.Get<IMsmqHeaderSerializer>() : new ExtensionHeaderSerializer();
                var transport = new MsmqTransport(inputQueueName, rebusLoggerFactory, extensionDeserializer);
                builder.Configure(transport);
                return transport;
            });

            return builder;
        }

        /// <summary>
        /// Configures Rebus to use MSMQ to transport messages as a one-way client (i.e. will not be able to receive any messages)
        /// </summary>
        public static MsmqTransportConfigurationBuilder UseMsmqAsOneWayClient(this StandardConfigurer<ITransport> configurer)
        {
            if (configurer == null) throw new ArgumentNullException(nameof(configurer));

            var builder = new MsmqTransportConfigurationBuilder();
            configurer.Register(c =>
            {
                var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                var extensionDeserializer = c.Has<IMsmqHeaderSerializer>(false) ? c.Get<IMsmqHeaderSerializer>() : new ExtensionHeaderSerializer();
                var transport = new MsmqTransport(null, rebusLoggerFactory, extensionDeserializer);
                builder.Configure(transport);
                return transport;
            });

            OneWayClientBackdoor.ConfigureOneWayClient(configurer);

            return builder;
        }
    }
}