using System;

namespace VRAutism.Cloud.LiveKit
{
    public interface ILiveKitRoomClient
    {
        public event Action OnSpeechMatched;

        public void Connect(string roomUrl, string token);
        public void Disconnect();
        
        public void SendActiveQuest(string questName, string[] defaultPhrases);

        public void EnableMicrophone(bool enable);

    }
}