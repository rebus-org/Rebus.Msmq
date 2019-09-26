using System.Collections.Generic;
using System.Messaging;
using System.Text;
using Rebus.Serialization;

namespace Rebus.Msmq
{
    /// <summary>
    /// Header serializer based on extension
    /// </summary>
    public class ExtensionHeaderSerializer : IMsmqHeaderSerializer
    {
        readonly HeaderSerializer _utf8HeaderSerializer = new HeaderSerializer { Encoding = Encoding.UTF8 };

        /// <summary>
        /// Serializes headers to the extension property of the msmq-message
        /// </summary>
        public void SerializeToMessage(Dictionary<string, string> headers, Message msmqMessage)
        {
            msmqMessage.Extension = _utf8HeaderSerializer.Serialize(headers);
        }

        /// <summary>
        /// Deserialize msmq-message from the extension property
        /// </summary>
        public Dictionary<string, string> Deserialize(Message msmqMessage)
        {
            return _utf8HeaderSerializer.Deserialize(msmqMessage.Extension);
        }
    }
}
