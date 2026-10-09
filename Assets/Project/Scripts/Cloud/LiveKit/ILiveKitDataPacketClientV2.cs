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

    /// <summary>
    /// Optional capability for cancellation packets that must survive their short-lived sender.
    /// Implementations should retain only the exact cancellation payload and a bounded amount of data.
    /// </summary>
    public interface ILiveKitDeferredDataPacketClientV2
    {
        bool PublishSpeakScriptCancellationV2(byte[] cancellationPacket);
    }
}
