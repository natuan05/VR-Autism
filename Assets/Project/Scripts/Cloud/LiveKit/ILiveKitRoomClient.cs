using System;
using UnityEngine;

namespace VRAutism.Cloud.LiveKit
{
    public interface ILiveKitRoomClient
    {
        event Action<string> OnAgentError;

        void Connect(string roomUrl, string token);
        void Disconnect();
        
        void EnableMicrophone(bool enable);
        void EnablePOVCamera(Camera vrCamera);
        void DisablePOVCamera();
    }
}
