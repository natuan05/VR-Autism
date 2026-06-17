using System;
using System.Collections;
using UnityEngine;
using Unity.WebRTC;

namespace VRAutism.Core.Telemetry
{
    public class WebRTCStreamer : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private int width = 1280;
        [SerializeField] private int height = 720;
        [SerializeField] private int frameRate = 30;
        
        public Action<string> OnLocalIceCandidate;
        public Action OnStreamConnected;
        public Action OnStreamDisconnected;

        private RTCPeerConnection peerConnection;
        private VideoStreamTrack videoTrack;
        private RenderTexture renderTexture;
        private Camera sourceCamera;
        private Camera captureCamera; // Camera phụ dành riêng cho WebRTC
        private bool isStreaming = false;
        private Coroutine webRtcUpdateCoroutine;
        private Coroutine manualRenderCoroutine;

        private void OnDestroy()
        {
            StopStreaming();
        }

        public IEnumerator StartStreaming(Camera camera, Action<string> onOfferCreated)
        {
            if (isStreaming) yield break;

            isStreaming = true;

            sourceCamera = camera;

            // CRITICAL: WebRTC internal update loop must run to process frames
            webRtcUpdateCoroutine = StartCoroutine(WebRTC.Update());

            // TẠO CAMERA PHỤ (SECONDARY CAMERA) ĐỂ KHÔNG LÀM ẢNH HƯỞNG CAMERA VR CHÍNH
            GameObject captureCamObj = new GameObject("WebRTC_CaptureCamera");
            captureCamObj.transform.SetParent(sourceCamera.transform, false); // Bám theo góc nhìn của trẻ
            
            captureCamera = captureCamObj.AddComponent<Camera>();
            captureCamera.CopyFrom(sourceCamera); // Copy y hệt FOV, Culling Mask,...
            captureCamera.targetDisplay = 8; // Đẩy ra màn hình ảo để không đè lên kính VR
            captureCamera.enabled = false; // Tắt tự động render để tiết kiệm hiệu năng trên Quest 2

            // Dùng camera phụ để encode WebRTC
            videoTrack = captureCamera.CaptureStreamTrack(width, height);

            // Bắt đầu luồng render thủ công theo tốc độ khung hình chỉ định
            manualRenderCoroutine = StartCoroutine(ManualRenderRoutine());

            // Lưu lại RenderTexture để dọn dẹp sau này
            renderTexture = captureCamera.targetTexture;

            // Initialize PeerConnection
            var config = GetRTCConfiguration();
            peerConnection = new RTCPeerConnection(ref config);

            // Setup callbacks
            peerConnection.OnIceCandidate = candidate =>
            {
                string json = JsonUtility.ToJson(new IceCandidateJson
                {
                    candidate = candidate.Candidate, // Chuỗi mô tả địa chỉ mạng cốt lõi
                    sdpMid = candidate.SdpMid, // Chuỗi định danh của luồng truyền thông đa phương tiện
                    sdpMLineIndex = candidate.SdpMLineIndex ?? 0 //Lấy số dòng (index) cấu hình
                });
                OnLocalIceCandidate?.Invoke(json);
            };

            peerConnection.OnIceConnectionChange = state =>
            {
                Debug.Log($"WebRTC ICE State: {state}");
                if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
                {
                    OnStreamConnected?.Invoke();
                }
                else if (state == RTCIceConnectionState.Disconnected || state == RTCIceConnectionState.Failed)
                {
                    OnStreamDisconnected?.Invoke();
                }
            };

            // Add track to peer connection
            peerConnection.AddTrack(videoTrack);

            // Create Offer
            var offerOperation = peerConnection.CreateOffer();
            yield return offerOperation;

            if (offerOperation.IsError)
            {
                Debug.LogError($"Error creating WebRTC offer: {offerOperation.Error.message}");
                StopStreaming();
                yield break;
            }

            var offerDesc = offerOperation.Desc;
            var localDescOp = peerConnection.SetLocalDescription(ref offerDesc);
            yield return localDescOp;

            if (localDescOp.IsError)
            {
                Debug.LogError($"Error setting local description: {localDescOp.Error.message}");
                StopStreaming();
                yield break;
            }

            // Convert SDP offer to JSON
            string sdpJson = JsonUtility.ToJson(new SessionDescriptionJson
            {
                type = offerDesc.type.ToString().ToLower(),
                sdp = offerDesc.sdp
            });

            onOfferCreated?.Invoke(sdpJson);
        }

        public void SetRemoteAnswer(string sdpJsonStr)
        {
            if (peerConnection == null) return;
            
            var sdpJson = JsonUtility.FromJson<SessionDescriptionJson>(sdpJsonStr);
            var answerDesc = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = sdpJson.sdp
            };
            
            var remoteDescOp = peerConnection.SetRemoteDescription(ref answerDesc);
            StartCoroutine(WaitForRemoteDesc(remoteDescOp));
        }

        private IEnumerator WaitForRemoteDesc(RTCSetSessionDescriptionAsyncOperation op)
        {
            yield return op;
            if (op.IsError)
            {
                Debug.LogError($"Error setting remote description: {op.Error.message}");
            }
        }

        public void AddIceCandidate(string candidateJsonStr)
        {
            if (peerConnection == null) return;

            var candJson = JsonUtility.FromJson<IceCandidateJson>(candidateJsonStr);
            var rtcIceCandidateInit = new RTCIceCandidateInit
            {
                candidate = candJson.candidate,
                sdpMid = candJson.sdpMid,
                sdpMLineIndex = candJson.sdpMLineIndex
            };
            var iceCandidate = new RTCIceCandidate(rtcIceCandidateInit);
            peerConnection.AddIceCandidate(iceCandidate);
        }

        public void StopStreaming()
        {
            if (!isStreaming) return;

            isStreaming = false;

            if (webRtcUpdateCoroutine != null)
            {
                StopCoroutine(webRtcUpdateCoroutine);
                webRtcUpdateCoroutine = null;
            }

            if (manualRenderCoroutine != null)
            {
                StopCoroutine(manualRenderCoroutine);
                manualRenderCoroutine = null;
            }

            if (captureCamera != null)
            {
                captureCamera.targetTexture = null;
                Destroy(captureCamera.gameObject);
                captureCamera = null;
            }

            if (sourceCamera != null)
            {
                sourceCamera = null;
            }

            if (videoTrack != null)
            {
                videoTrack.Dispose();
                videoTrack = null;
            }

            if (peerConnection != null)
            {
                peerConnection.Close();
                peerConnection.Dispose();
                peerConnection = null;
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }

        private IEnumerator ManualRenderRoutine()
        {
            float interval = 1f / frameRate;
            var wait = new WaitForSeconds(interval);
            while (captureCamera != null)
            {
                captureCamera.Render();
                yield return wait;
            }
        }

        private RTCConfiguration GetRTCConfiguration()
        {
            RTCConfiguration config = default;
            config.iceServers = new[]
            {
                new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
            };
            return config;
        }

        [Serializable]
        private class SessionDescriptionJson
        {
            public string type;
            public string sdp;
        }

        [Serializable]
        private class IceCandidateJson
        {
            public string candidate;
            public string sdpMid;
            public int sdpMLineIndex;
        }
    }
}
