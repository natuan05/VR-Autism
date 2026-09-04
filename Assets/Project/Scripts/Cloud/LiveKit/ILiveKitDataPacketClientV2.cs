using System;

namespace VRAutism.Cloud.LiveKit
{
    public interface ILiveKitDataPacketClientV2
    {
        bool IsConnectedV2 { get; }
        event Action<byte[], string> DataReceivedV2;
        event Action ReconnectedV2;
        void PublishDataV2(byte[] data, string topic, bool reliable);
    }
}
