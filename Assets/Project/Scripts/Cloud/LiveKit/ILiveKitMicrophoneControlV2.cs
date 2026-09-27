namespace VRAutism.Cloud.LiveKit
{
    /// <summary>Controls the microphone track owned by the LiveKit room client.</summary>
    public interface ILiveKitMicrophoneControlV2
    {
        void EnableMicrophone(bool enable);
    }
}
