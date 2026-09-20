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
                if (!_registrations.TryGetValue(handle, out registration))
                    return;

                if (ReferenceEquals(_currentHandle, handle))
                    _currentHandle = null;
            }

            registration.DetachAndDisconnect();

            lock (_gate)
            {
                if (registration.CleanupComplete)
                    _registrations.Remove(handle);
            }
        }

        internal bool IsCleanupComplete(RoomConnectionHandle handle)
        {
            if (handle == null)
                return true;

            lock (_gate)
                return !_registrations.ContainsKey(handle);
        }

        internal RoomConnectionHandle[] GetPendingCleanupHandles()
        {
            lock (_gate)
            {
                var handles = new List<RoomConnectionHandle>();
                foreach (var registration in _registrations.Values)
                {
                    if (registration.Detached)
                        handles.Add(registration.Handle);
                }

                return handles.ToArray();
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
            registration.MarkDataReceivedAttached();
            adapter.Reconnected += registration.Reconnected;
            registration.MarkReconnectedAttached();
            adapter.TrackSubscribed += registration.TrackSubscribed;
            registration.MarkTrackSubscribedAttached();
            adapter.TrackUnsubscribed += registration.TrackUnsubscribed;
            registration.MarkTrackUnsubscribedAttached();
        }

        private sealed class Registration
        {
            private readonly LiveKitRoomConnection _owner;
            internal readonly RoomConnectionHandle Handle;
            internal readonly Action<byte[], Participant, DataPacketKind, string> DataReceived;
            internal readonly Action<Room> Reconnected;
            internal readonly Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackSubscribed;
            internal readonly Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> TrackUnsubscribed;
            private readonly object _cleanupGate = new object();
            private bool _dataReceivedAttached;
            private bool _reconnectedAttached;
            private bool _trackSubscribedAttached;
            private bool _trackUnsubscribedAttached;
            private bool _disconnectComplete;
            internal bool Detached { get; private set; }

            internal void MarkDataReceivedAttached() => _dataReceivedAttached = true;

            internal void MarkReconnectedAttached() => _reconnectedAttached = true;

            internal void MarkTrackSubscribedAttached() => _trackSubscribedAttached = true;

            internal void MarkTrackUnsubscribedAttached() => _trackUnsubscribedAttached = true;

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

            internal bool CleanupComplete
            {
                get
                {
                    lock (_cleanupGate)
                    {
                        return !_dataReceivedAttached && !_reconnectedAttached &&
                               !_trackSubscribedAttached && !_trackUnsubscribedAttached &&
                               _disconnectComplete;
                    }
                }
            }

            internal void DetachAndDisconnect()
            {
                lock (_cleanupGate)
                {
                    // Suppress callbacks before touching the SDK. Each unsubscribe is
                    // independently retryable if an adapter throws.
                    Detached = true;
                    TryDetach(
                        ref _dataReceivedAttached,
                        () => Handle.Adapter.DataReceived -= DataReceived,
                        "data");
                    TryDetach(
                        ref _reconnectedAttached,
                        () => Handle.Adapter.Reconnected -= Reconnected,
                        "reconnect");
                    TryDetach(
                        ref _trackSubscribedAttached,
                        () => Handle.Adapter.TrackSubscribed -= TrackSubscribed,
                        "track subscribed");
                    TryDetach(
                        ref _trackUnsubscribedAttached,
                        () => Handle.Adapter.TrackUnsubscribed -= TrackUnsubscribed,
                        "track unsubscribed");

                    // Disconnect is deliberately independent from delegate detachment.
                    if (!_disconnectComplete)
                    {
                        try
                        {
                            Handle.Adapter.Disconnect();
                            _disconnectComplete = true;
                        }
                        catch (Exception exception)
                        {
                            UnityEngine.Debug.LogWarning(
                                $"[LiveKitService] Room disconnect cleanup notice: {exception.Message}");
                        }
                    }
                }
            }

            private static void TryDetach(ref bool attached, Action detach, string delegateName)
            {
                if (!attached)
                    return;

                try
                {
                    detach();
                    attached = false;
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[LiveKitService] Room {delegateName} detach cleanup notice: {exception.Message}");
                }
            }

        }
    }
}
