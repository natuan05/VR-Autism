using System.Collections;
using UnityEngine;

namespace VRAutism.Cloud.LiveKit
{
    internal interface ILiveKitCoroutineHost
    {
        Coroutine StartLiveKitCoroutine(IEnumerator routine);
        void StopLiveKitCoroutine(Coroutine coroutine);
    }
}
