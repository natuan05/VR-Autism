using System;
using System.Threading.Tasks;
using LiveKit;
using LiveKit.Proto;

namespace VRAutism.Cloud.LiveKit
{
    internal interface ILiveKitRoomAdapter
    {
        Room SdkRoom { get; }
        bool IsConnected { get; }
        string RoomName { get; }
        string LocalParticipantSid { get; }
        event Action<byte[], Participant, DataPacketKind, string> DataReceived;
        event Action<Room> Reconnected;
        event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;
        event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;
        Task ConnectAsync(string roomUrl, string token);
        void PublishData(byte[] data, string topic, bool reliable);
        Task PublishAudioTrackAsync(LocalAudioTrack track, TrackPublishOptions options);
        Task PublishVideoTrackAsync(LocalVideoTrack track, TrackPublishOptions options);
        void UnpublishAudioTrack(LocalAudioTrack track);
        void UnpublishVideoTrack(LocalVideoTrack track);
        void Disconnect();
    }

    internal interface ILiveKitRoomAdapterFactory
    {
        ILiveKitRoomAdapter Create();
    }
}
