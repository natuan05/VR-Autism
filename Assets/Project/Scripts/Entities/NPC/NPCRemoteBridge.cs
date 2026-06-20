using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using VRAutism.Cloud.RTDB;

namespace VRAutism.Entities
{
    public class NPCRemoteBridge : MonoBehaviour
    {
        [SerializeField] private NPCController npcController;

        private void Awake()
        {
            if (npcController == null)
            {
                npcController = GetComponent<NPCController>();
            }
        }

        private void OnEnable()
        {
            RemoteCommandListener.OnPlayNpcScript += HandlePlayNpcScript;
        }

        private void OnDisable()
        {
            RemoteCommandListener.OnPlayNpcScript -= HandlePlayNpcScript;
        }

        private void HandlePlayNpcScript(string paramStr)
        {
            if (npcController == null)
            {
                Debug.LogWarning("[NPCRemoteBridge] npcController is null. Cannot execute remote command.");
                return;
            }

            string url = paramStr;
            string subtitleText = "";

            if (paramStr.Contains("|||"))
            {
                string[] parts = paramStr.Split(new[] { "|||" }, System.StringSplitOptions.None);
                url = parts[0];
                subtitleText = parts.Length > 1 ? parts[1] : "";
            }

            string originalText = subtitleText;
            if (string.IsNullOrEmpty(originalText))
            {
                if (!string.IsNullOrEmpty(url) && url.Contains("q="))
                {
                    try
                    {
                        int startIndex = url.IndexOf("q=") + 2;
                        int endIndex = url.IndexOf("&", startIndex);
                        string encodedText = endIndex > 0 ? url.Substring(startIndex, endIndex - startIndex) : url.Substring(startIndex);
                        originalText = System.Uri.UnescapeDataString(encodedText);
                    }
                    catch
                    {
                        originalText = "NPC is speaking...";
                    }
                }
                else
                {
                    originalText = url;
                }
            }

            if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
            {
                Debug.LogWarning("[NPCRemoteBridge] Invalid URL or local text received: " + url);
                npcController.PlayRemoteText(originalText);
                return;
            }

            StartCoroutine(DownloadAndPlayVoice(url, originalText));
        }

        private IEnumerator DownloadAndPlayVoice(string url, string originalText)
        {
            using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                uwr.timeout = 3;
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.ConnectionError || 
                    uwr.result == UnityWebRequest.Result.ProtocolError || 
                    uwr.result == UnityWebRequest.Result.DataProcessingError)
                {
                    Debug.LogWarning($"[NPCRemoteBridge] Lỗi tải âm thanh: {uwr.error}. Sử dụng bong bóng thoại fallback.");
                    npcController.PlayRemoteText(originalText);
                }
                else
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
                    if (clip != null)
                    {
                        npcController.PlayRemoteVoice(clip, originalText);
                    }
                    else
                    {
                        Debug.LogWarning("[NPCRemoteBridge] AudioClip null. Sử dụng bong bóng thoại fallback.");
                        npcController.PlayRemoteText(originalText);
                    }
                }
            }
        }
    }
}
