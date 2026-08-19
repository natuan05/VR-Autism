using System;
using UnityEngine;

namespace VRAutism.Cloud.LiveKit
{
    public interface ILiveKitRoomClient
    {
        event Action OnSpeechMatched;
        event Action<string> OnAgentError;
        event Action<string, string> OnQuestStatusUpdate;

        void Connect(string roomUrl, string token);
        void Disconnect();
        
        void SendActiveQuest(string questName, string[] defaultPhrases);
        void SendVerbalHint();
        void SendOnReminder();

        void EnableMicrophone(bool enable);
        void EnablePOVCamera(Camera vrCamera);
        void DisablePOVCamera();
    }
}