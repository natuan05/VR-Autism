using System;
using LiveKit;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class RoomConnectionHandle
    {
        public long Generation { get; }
        public ILiveKitRoomAdapter Adapter { get; }
        public Room SdkRoom => Adapter.SdkRoom;
        public bool IsConnected => Adapter.IsConnected;

        public RoomConnectionHandle(long generation, ILiveKitRoomAdapter adapter)
        {
            Generation = generation;
            Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }
    }
}
