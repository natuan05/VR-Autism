using System;
using LiveKit;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class LiveKitDataPacketTransport
    {
        private const string V2Topic = "lesson-graph-v2.voice";
        private readonly Func<RoomConnectionHandle> _currentHandle;

        internal event Action<byte[], string> DataReceivedV2;
        internal event Action<byte[], string> LegacyDataReceived;

        internal LiveKitDataPacketTransport(Func<RoomConnectionHandle> currentHandle)
        {
            _currentHandle = currentHandle ?? throw new ArgumentNullException(nameof(currentHandle));
        }

        internal void PublishDataV2(byte[] data, string topic, bool reliable)
        {
            Publish(data, topic, reliable);
        }

        internal void PublishLegacy(byte[] data, bool reliable)
        {
            Publish(data, null, reliable);
        }

        internal void HandleIncoming(byte[] data, Participant participant, string topic)
        {
            var copy = data == null ? null : (byte[])data.Clone();
            if (string.Equals(topic, V2Topic, StringComparison.Ordinal))
            {
                DataReceivedV2?.Invoke(copy, topic);
                return;
            }

            LegacyDataReceived?.Invoke(copy, topic);
        }

        private void Publish(byte[] data, string topic, bool reliable)
        {
            if (data == null)
            {
                return;
            }

            var handle = _currentHandle();
            if (handle == null || !handle.IsConnected || handle.Adapter == null)
            {
                return;
            }

            handle.Adapter.PublishData(data, topic, reliable);
        }
    }
}
