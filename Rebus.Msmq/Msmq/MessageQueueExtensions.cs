using System;
using System.Messaging;
using System.Runtime.InteropServices;

namespace Rebus.Msmq
{
    /// <summary>
    /// http://functionalflow.co.uk/blog/2008/08/27/counting-the-number-of-messages-in-a-message-queue-in/
    /// </summary>
    static class MessageQueueExtensions
    {
        [DllImport("mqrt.dll")]
        static extern unsafe int MQMgmtGetInfo(char* computerName, char* objectName, MQMGMTPROPS* mgmtProps);

        const byte VT_NULL = 1;
        const byte VT_UI4 = 19;
        const int PROPID_MGMT_QUEUE_MESSAGE_COUNT = 7;

        //size must be 16
        [StructLayout(LayoutKind.Sequential)]
        struct MQPROPVariant
        {
            public byte vt;       //0
            public byte spacer;   //1
            public short spacer2; //2
            public int spacer3;   //4
            public uint ulVal;    //8
            public int spacer4;   //12
        }

        //size must be 16 in x86 and 28 in x64
        [StructLayout(LayoutKind.Sequential)]
        unsafe struct MQMGMTPROPS
        {
            public uint cProp;
            public int* aPropID;
            public MQPROPVariant* aPropVar;
            public int* status;
        }

        public static uint GetCount(this MessageQueue queue)
        {
            return GetCount(queue.Path);
        }

        public static unsafe uint GetCount(string path)
        {
            if (!MessageQueue.Exists(path))
            {
                return 0;
            }

            MQMGMTPROPS props = new MQMGMTPROPS();
            props.cProp = 1;

            int aPropId = PROPID_MGMT_QUEUE_MESSAGE_COUNT;
            props.aPropID = &aPropId;

            MQPROPVariant aPropVar = new MQPROPVariant();
            aPropVar.vt = VT_NULL;
            props.aPropVar = &aPropVar;

            int status = 0;
            props.status = &status;

            IntPtr objectName = Marshal.StringToBSTR("queue=Direct=OS:" + path);
            try
            {
                int result = MQMgmtGetInfo(null, (char*)objectName, &props);
                if (result != 0 || *props.status != 0 || props.aPropVar->vt != VT_UI4)
                {
                    return 0;
                }
                else
                {
                    return props.aPropVar->ulVal;
                }
            }
            finally
            {
                Marshal.FreeBSTR(objectName);
            }
        }
    }
}