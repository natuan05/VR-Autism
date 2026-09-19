using System;
using System.Threading.Tasks;
using LiveKit;
using LiveKit.Proto;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class LiveKitRoomAdapter : ILiveKitRoomAdapter
    {
        private readonly Room _room;

        private Action<byte[], Participant, DataPacketKind, string> _dataReceived;
        private Action<Room> _reconnected;
        private Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> _trackSubscribed;
        private Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> _trackUnsubscribed;

        public LiveKitRoomAdapter()
            : this(new Room())
        {
        }

        internal LiveKitRoomAdapter(Room room)
        {
            _room = room ?? throw new ArgumentNullException(nameof(room));
            _room.DataReceived += HandleDataReceived;
            _room.Reconnected += HandleReconnected;
            _room.TrackSubscribed += HandleTrackSubscribed;
            _room.TrackUnsubscribed += HandleTrackUnsubscribed;
        }

        public Room SdkRoom => _room;
        public bool IsConnected => _room.IsConnected;
        public string RoomName => _room.Name;
        public string LocalParticipantSid => _room.LocalParticipant?.Sid;

        public event Action<byte[], Participant, DataPacketKind, string> DataReceived
        {
            add => _dataReceived += value;
            remove => _dataReceived -= value;
        }

        public event Action<Room> Reconnected
        {
            add => _reconnected += value;
            remove => _reconnected -= value;
        }

        public event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed
        {
            add => _trackSubscribed += value;
            remove => _trackSubscribed -= value;
        }

        public event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed
        {
            add => _trackUnsubscribed += value;
            remove => _trackUnsubscribed -= value;
        }

        public async Task ConnectAsync(string roomUrl, string token)
        {
            await _room.Connect(roomUrl, token, new global::LiveKit.RoomOptions());
        }

        public void PublishData(byte[] data, string topic, bool reliable)
        {
            if (topic == null)
            {
                _room.LocalParticipant.PublishData(data, reliable: reliable);
                return;
            }

            _room.LocalParticipant.PublishData(data, null, reliable, topic);
        }

        public async Task PublishAudioTrackAsync(LocalAudioTrack track, TrackPublishOptions options)
        {
            await _room.LocalParticipant.PublishTrack(track, options);
        }

        public async Task PublishVideoTrackAsync(LocalVideoTrack track, TrackPublishOptions options)
        {
            await _room.LocalParticipant.PublishTrack(track, options);
        }

        public void UnpublishAudioTrack(LocalAudioTrack track)
        {
            _room.LocalParticipant.UnpublishTrack(track, false);
        }

        public void UnpublishVideoTrack(LocalVideoTrack track)
        {
            _room.LocalParticipant.UnpublishTrack(track, false);
        }

        public void Disconnect()
        {
            _room.DataReceived -= HandleDataReceived;
            _room.Reconnected -= HandleReconnected;
            _room.TrackSubscribed -= HandleTrackSubscribed;
            _room.TrackUnsubscribed -= HandleTrackUnsubscribed;
            _room.Disconnect();
        }

        private void HandleDataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
        {
            _dataReceived?.Invoke(data, participant, kind, topic);
        }

        private void HandleReconnected(Room room)
        {
            _reconnected?.Invoke(room);
        }

        private void HandleTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            _trackSubscribed?.Invoke(track, publication, participant);
        }

        private void HandleTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
        {
            _trackUnsubscribed?.Invoke(track, publication, participant);
        }
    }

    internal sealed class LiveKitRoomAdapterFactory : ILiveKitRoomAdapterFactory
    {
        public ILiveKitRoomAdapter Create()
        {
            return new LiveKitRoomAdapter();
        }
    }
}
