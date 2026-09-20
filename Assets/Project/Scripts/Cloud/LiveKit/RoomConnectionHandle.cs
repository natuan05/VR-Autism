using System;
using LiveKit;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class RoomConnectionHandle
    {
        private readonly object _teardownGate = new object();
        private bool _povTeardownComplete;
        private bool _microphoneTeardownComplete;
        private bool _audioTeardownComplete;

        public long Generation { get; }
        public ILiveKitRoomAdapter Adapter { get; }
        public Room SdkRoom => Adapter.SdkRoom;
        public bool IsConnected => Adapter.IsConnected;

        public RoomConnectionHandle(long generation, ILiveKitRoomAdapter adapter)
        {
            Generation = generation;
            Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        internal bool RunPovTeardown(Action teardown) => RunTeardown(ref _povTeardownComplete, teardown);

        internal bool RunMicrophoneTeardown(Action teardown) =>
            RunTeardown(ref _microphoneTeardownComplete, teardown);

        internal bool RunAudioTeardown(Action teardown) => RunTeardown(ref _audioTeardownComplete, teardown);

        internal bool DeviceTeardownComplete
        {
            get
            {
                lock (_teardownGate)
                    return _povTeardownComplete && _microphoneTeardownComplete && _audioTeardownComplete;
            }
        }

        private bool RunTeardown(ref bool completed, Action teardown)
        {
            if (teardown == null) throw new ArgumentNullException(nameof(teardown));

            lock (_teardownGate)
            {
                if (completed)
                    return true;

                try
                {
                    teardown();
                    completed = true;
                    return true;
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[LiveKitService] Lifecycle teardown notice: {exception.Message}");
                    return false;
                }
            }
        }
    }
}
