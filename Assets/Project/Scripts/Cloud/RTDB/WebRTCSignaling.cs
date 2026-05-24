using System;
using System.Collections;
using UnityEngine;
using Firebase.Database;
using Firebase.Extensions;

namespace VRAutism.Cloud.RTDB
{
    public class WebRTCSignaling : MonoBehaviour
    {
        public Action<string> OnAnswerReceived;
        public Action<string> OnIceCandidateReceived;
        public Action OnSignalingFailed;


        private string currentSessionId;
        private bool isListening = false;
        private Coroutine timeoutCoroutine;

        private DatabaseReference GetRoot() => RTDBConnection.Instance?.RootRef;

        private void OnDestroy()
        {
            Cleanup();
        }

        public void InitiateSignaling(string sessionId, string sdpOfferJson) //sessionID: TimeManager -> LiveSessionReporter -> WebRTCManager -> WebRTCSig/ Stream
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            currentSessionId = sessionId;
            var root = GetRoot();
            if (root == null) { OnSignalingFailed?.Invoke(); return; }
            var sessionRef = root.Child(FirebasePaths.WebRTCSignaling).Child(sessionId);

            // Ghi Offer
            sessionRef.Child(FirebasePaths.WebRTCOffer).SetValueAsync(sdpOfferJson).ContinueWithOnMainThread(task =>
            {
                if (task.IsFaulted)
                {
                    Debug.LogError("Failed to write WebRTC offer to RTDB.");
                    OnSignalingFailed?.Invoke();
                    return;
                }

                // Lắng nghe Answer
                sessionRef.Child(FirebasePaths.WebRTCAnswer).ValueChanged += HandleAnswerChanged;
                
                // Lắng nghe Web Candidates
                sessionRef.Child(FirebasePaths.WebRTCWebCandidates).ChildAdded += HandleWebCandidateAdded;
                
                isListening = true;
                Debug.Log("WebRTC Signaling initialized, waiting for answer...");
                
                if (timeoutCoroutine != null) StopCoroutine(timeoutCoroutine);
                timeoutCoroutine = StartCoroutine(SignalingTimeoutRoutine());
            });
        }

        private IEnumerator SignalingTimeoutRoutine()
        {
            yield return new WaitForSeconds(15f);
            Debug.LogWarning("WebRTC Signaling timeout (15s). No answer received.");
            OnSignalingFailed?.Invoke();
        }

        public void SendIceCandidate(string candidateJson)
        {
            if (string.IsNullOrEmpty(currentSessionId)) return;

            var root = GetRoot();
            if (root == null) return;
            string pushId = root.Push().Key;
            root.Child(FirebasePaths.WebRTCSignaling)
                .Child(currentSessionId)
                .Child(FirebasePaths.WebRTCVRCandidates)
                .Child(pushId)
                .SetValueAsync(candidateJson);
        }

        private void HandleAnswerChanged(object sender, ValueChangedEventArgs args)
        {
            if (args.DatabaseError != null) return;
            if (args.Snapshot == null || !args.Snapshot.Exists) return;

            if (timeoutCoroutine != null)
            {
                StopCoroutine(timeoutCoroutine);
                timeoutCoroutine = null;
            }

            string answerJson = args.Snapshot.Value.ToString();
            Debug.Log("WebRTC Answer received.");
            OnAnswerReceived?.Invoke(answerJson);
        }

        private void HandleWebCandidateAdded(object sender, ChildChangedEventArgs args)
        {
            if (args.DatabaseError != null) return;
            if (args.Snapshot == null || !args.Snapshot.Exists) return;

            string candidateJson = args.Snapshot.Value.ToString();
            Debug.Log("WebRTC remote ICE candidate received.");
            OnIceCandidateReceived?.Invoke(candidateJson);
        }

        public void Cleanup()
        {
            if (timeoutCoroutine != null)
            {
                StopCoroutine(timeoutCoroutine);
                timeoutCoroutine = null;
            }

            if (!isListening || string.IsNullOrEmpty(currentSessionId)) return;

            var root = GetRoot();
            if (root == null) return;
            var sessionRef = root.Child(FirebasePaths.WebRTCSignaling).Child(currentSessionId);
            
            sessionRef.Child(FirebasePaths.WebRTCAnswer).ValueChanged -= HandleAnswerChanged;
            sessionRef.Child(FirebasePaths.WebRTCWebCandidates).ChildAdded -= HandleWebCandidateAdded;

            // Xóa nhánh signaling để dọn rác
            sessionRef.RemoveValueAsync();
            
            isListening = false;
            currentSessionId = null;
            Debug.Log("WebRTC Signaling cleaned up.");
        }
    }
}
