using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiveKit;
using LiveKit.Proto;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class LiveKitRoomConnection
    {
        private readonly ILiveKitRoomAdapterFactory _adapterFactory;
        private readonly object _gate = new object();
        private readonly Dictionary<RoomConnectionHandle, Registration> _registrations =
            new Dictionary<RoomConnectionHandle, Registration>();
        private RoomConnectionHandle _currentHandle;

        internal LiveKitRoomConnection(ILiveKitRoomAdapterFactory adapterFactory)
        {
            _adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
        }

        internal RoomConnectionHandle CurrentHandle
        {
            get
            {
                lock (_gate)
                    return _currentHandle;
            }
        }

        internal event Action<RoomConnectionHandle, byte[], Participant, DataPacketKind, string> DataReceived;
        internal event Action<RoomConnectionHandle> Reconnected;
        internal event Action<RoomConnectionHandle, IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;
        internal event Action<RoomConnectionHandle, IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;

        internal async Task<RoomConnectionHandle> CreateConnectedAsync(
            long generation,
            string roomUrl,
            string token,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var adapter = _adapterFactory.Create() ?? throw new InvalidOperationException("Room adapter factory returned null.");
            var handle = new RoomConnectionHandle(generation, adapter);
            var registration = new Registration(this, handle);
            lock (_gate)
                _registrations.Add(handle, registration);

            Attach(registration);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await adapter.ConnectAsync(roomUrl, token);
                cancellationToken.ThrowIfCancellationRequested();
                return handle;
            }
            catch
            {
                DetachAndDisconnect(handle);
                throw;
            }
        }

        internal void Commit(RoomConnectionHandle handle)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));

            lock (_gate)
            {
                if (!_registrations.TryGetValue(handle, out var registration) || registration.Detached)
                    throw new InvalidOperationException("Cannot commit a detached room connection.");

                _currentHandle = handle;
            }
        }

        internal void DetachAndDisconnect(RoomConnectionHandle handle)
        {
            if (handle == null)
                return;

            Registration registration;
            lock (_gate)
            {
                if (!_registrations.TryGetValue(handle, out registration) || registration.Detached)
                    return;

                registration.Detached = true;
                _registrations.Remove(handle);
                if (ReferenceEquals(_currentHandle, handle))
                    _currentHandle = null;
            }

            registration.Detach();
            try
            {
                registration.Handle.Adapter.Disconnect();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"[LiveKitService] Room disconnect cleanup notice: {exception.Message}");
            }
        }

        internal bool IsCurrent(long generation)
        {
            lock (_gate)
                return _currentHandle != null && _currentHandle.Generation == generation;
        }

        private void Attach(Registration registration)
        {
            var adapter = registration.Handle.Adapter;
            adapter.DataReceived += registration.DataReceived;
            adapter.Reconnected += registration.Reconnected;
            adapter.TrackSubscribed += registration.TrackSubscribed;
            adapter.TrackUnsubscribed += registration.TrackUnsubscribed;
        }

        private sealed class Registration
        {
            private readonly LiveKitRoomConnection _owner;
            internal readonly RoomConnectionHandle Handle;
            internal readonly Action<byte[], Participant, DataPacketKind, string> DataReceived;
            internal readonly Action<Room> Reconnected;
            internal readonly Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;
            internal readonly Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;
            internal bool Detached;

            internal Registration(LiveKitRoomConnection owner, RoomConnectionHandle handle)
            {
                _owner = owner;
                Handle = handle;
                DataReceived = (data, participant, kind, topic) =>
                {
                    if (!Detached)
                        _owner.DataReceived?.Invoke(handle, data, participant, kind, topic);
                };
                Reconnected = room =>
                {
                    if (!Detached)
                        _owner.Reconnected?.Invoke(handle);
                };
                TrackSubscribed = (track, publication, participant) =>
                {
                    if (!Detached)
                        _owner.TrackSubscribed?.Invoke(handle, track, publication, participant);
                };
                TrackUnsubscribed = (track, publication, participant) =>
                {
                    if (!Detached)
                        _owner.TrackUnsubscribed?.Invoke(handle, track, publication, participant);
                };
            }

            internal void Detach()
            {
                var adapter = Handle.Adapter;
                adapter.DataReceived -= DataReceived;
                adapter.Reconnected -= Reconnected;
                adapter.TrackSubscribed -= TrackSubscribed;
                adapter.TrackUnsubscribed -= TrackUnsubscribed;
            }

        }
    }
}
