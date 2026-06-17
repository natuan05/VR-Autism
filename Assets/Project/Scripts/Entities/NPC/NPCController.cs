using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using VRAutism.Core;
using VRAutism.Cloud.RTDB;

namespace VRAutism.Entities
{
    public class NPCController : MonoBehaviour
    {
        [SerializeField] private AudioSource[] npcs;
        [SerializeField] private AudioClip[] audioClips;
        [SerializeField] private ReminderData[] reminders;
        [SerializeField] private IntVariable hintCount;
        
        [SerializeField] private SpeechResponser[] speechResponser;

        private AudioSource myNPC;
        private GameObject _activeBubble;

        private Sprite _roundedBorderSprite;
        private Sprite _roundedBackgroundSprite;
        private Sprite _tailSprite;
        private Sprite _innerTailSprite;

        private void OnEnable()
        {
            RemoteCommandListener.OnPlayNpcScript += HandlePlayNpcScript;
        }

        private void OnDisable()
        {
            RemoteCommandListener.OnPlayNpcScript -= HandlePlayNpcScript;
        }

        public void SetNpc(int id)
        {
            myNPC = npcs[id];
        }

        private void Start()
        {
            foreach (var responser in speechResponser)
            {
                responser.OnPrompt += SayAudio;
            }

            // Initialize default NPC if not set
            if (myNPC == null && npcs != null && npcs.Length > 0)
            {
                myNPC = npcs[0];
            }
        }
        
        public void SaySomething(int id)
        {
            myNPC.clip = audioClips[id];
            myNPC.Play();
           // myNPC.PlayOneShot(audioClips[id]);
        }

        public void SayAudio(AudioClip clip)
        {
            myNPC.clip = clip;
            myNPC.Play();
        }

        public void SayRandomReminder(int id)
        {
            hintCount.Value++;
            myNPC.clip = reminders[id].audioClips[Random.Range(0, reminders[id].audioClips.Length)];
            myNPC.Play();
        }

        private void HandlePlayNpcScript(string paramStr)
        {
            if (myNPC == null && npcs != null && npcs.Length > 0)
            {
                myNPC = npcs[0];
            }

            if (myNPC == null)
            {
                Debug.LogWarning("[NPCController] myNPC is null and npcs list is empty. Cannot play NPC script.");
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

            StartCoroutine(DownloadAndPlayVoice(url, subtitleText));
        }

        private IEnumerator DownloadAndPlayVoice(string url, string subtitleText = "")
        {
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
                Debug.LogWarning("[NPCController] Invalid URL or local text received: " + url);
                ShowSpeechBubble(originalText, 5.0f);
                yield break;
            }

            // Xoay NPC nhìn về phía người chơi trong 3 giây
            StartCoroutine(SmoothLookAtPlayer(3.0f));

            using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                uwr.timeout = 3;
                yield return uwr.SendWebRequest();

                if (uwr.result == UnityWebRequest.Result.ConnectionError || 
                    uwr.result == UnityWebRequest.Result.ProtocolError || 
                    uwr.result == UnityWebRequest.Result.DataProcessingError)
                {
                    Debug.LogWarning($"[NPCController] Lỗi tải âm thanh: {uwr.error}. Sử dụng bong bóng thoại fallback.");
                    ShowSpeechBubble(originalText, 5.0f);
                }
                else
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
                    if (clip != null)
                    {
                        float bubbleDuration = Mathf.Max(3.0f, clip.length + 0.5f);
                        ShowSpeechBubble(originalText, bubbleDuration);
                        StartCoroutine(FadeInAndPlay(clip));
                    }
                    else
                    {
                        Debug.LogWarning("[NPCController] AudioClip null. Sử dụng bong bóng thoại fallback.");
                        ShowSpeechBubble(originalText, 5.0f);
                    }
                }
            }
        }

        private IEnumerator SmoothLookAtPlayer(float duration)
        {
            if (myNPC == null) yield break;

            Transform npcTransform = myNPC.transform;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (Camera.main == null) yield break;

                Vector3 lookDirection = Camera.main.transform.position - npcTransform.position;
                lookDirection.y = 0; // Giữ NPC đứng thẳng
                if (lookDirection != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDirection);
                    npcTransform.rotation = Quaternion.Slerp(npcTransform.rotation, targetRot, Time.deltaTime * 3f);
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator FadeInAndPlay(AudioClip clip)
        {
            if (myNPC == null) yield break;

            myNPC.clip = clip;
            myNPC.volume = 0f;
            myNPC.Play();

            float maxVolume = 0.5f;
            if (SessionContext.Instance != null)
            {
                maxVolume = SessionContext.Instance.MaxVolume;
            }

            float duration = 0.5f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                myNPC.volume = Mathf.Lerp(0f, maxVolume, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }
            myNPC.volume = maxVolume;
        }

        private void ShowSpeechBubble(string text, float duration = 5.0f)
        {
            if (_activeBubble != null)
            {
                Destroy(_activeBubble);
            }

            if (myNPC == null) return;

            // Tạo Canvas trong World Space
            GameObject canvasGo = new GameObject("NPCSpeechBubble");
            canvasGo.transform.SetParent(myNPC.transform);
            canvasGo.transform.localPosition = new Vector3(0, 1.85f, 0); // Nhích lên một chút để tránh lấp đầu NPC
            canvasGo.transform.localRotation = Quaternion.identity;

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            UnityEngine.UI.CanvasScaler scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10;

            // Chuẩn bị các sprite bo tròn động
            PrepareRoundedSprites();

            // Viền ngoài của Bong bóng thoại (Border Panel)
            GameObject borderGo = new GameObject("Border");
            borderGo.transform.SetParent(canvasGo.transform, false);
            var borderImage = borderGo.AddComponent<UnityEngine.UI.Image>();
            if (_roundedBorderSprite != null)
            {
                borderImage.sprite = _roundedBorderSprite;
                borderImage.type = UnityEngine.UI.Image.Type.Sliced;
            }
            borderImage.color = Color.white;

            // Đuôi viền của bong bóng thoại (Border Tail)
            GameObject borderTailGo = new GameObject("BorderTail");
            borderTailGo.transform.SetParent(borderGo.transform, false);
            var borderTailImage = borderTailGo.AddComponent<UnityEngine.UI.Image>();
            if (_tailSprite != null)
            {
                borderTailImage.sprite = _tailSprite;
            }

            // Nền trong của Bong bóng thoại (Background Panel)
            GameObject panelGo = new GameObject("Background");
            panelGo.transform.SetParent(borderGo.transform, false);
            var panelImage = panelGo.AddComponent<UnityEngine.UI.Image>();
            if (_roundedBackgroundSprite != null)
            {
                panelImage.sprite = _roundedBackgroundSprite;
                panelImage.type = UnityEngine.UI.Image.Type.Sliced;
            }
            panelImage.color = Color.white;

            // Đuôi nền của bong bóng thoại (Inner Tail)
            GameObject innerTailGo = new GameObject("InnerTail");
            innerTailGo.transform.SetParent(borderGo.transform, false);
            var innerTailImage = innerTailGo.AddComponent<UnityEngine.UI.Image>();
            if (_innerTailSprite != null)
            {
                innerTailImage.sprite = _innerTailSprite;
            }

            // Text hiển thị lời thoại
            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(panelGo.transform, false);
            var tmpText = textGo.AddComponent<TMPro.TextMeshProUGUI>();
            tmpText.text = text;
            tmpText.fontSize = 14;
            tmpText.fontSizeMin = 8;
            tmpText.fontSizeMax = 16;
            tmpText.enableAutoSizing = true;
            tmpText.color = new Color(0.09f, 0.09f, 0.11f, 1.0f); // Chữ màu xám than sang trọng (#17171B)
            tmpText.alignment = TMPro.TextAlignmentOptions.Center;

            // Thiết lập kích thước hệ thống UI RectTransform
            RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(320, 110);
            canvasRect.localScale = new Vector3(0.003f, 0.003f, 0.003f); // Vừa vặn trong không gian 3D VR

            RectTransform borderRect = borderGo.GetComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.sizeDelta = Vector2.zero;

            RectTransform borderTailRect = borderTailGo.GetComponent<RectTransform>();
            borderTailRect.sizeDelta = new Vector2(16, 16);
            borderTailRect.anchoredPosition = new Vector2(0, -55f); // Đặt ở đáy bong bóng thoại
            borderTailRect.localRotation = Quaternion.Euler(0, 0, 45); // Xoay 45 độ tạo hình thoi chỉ xuống

            RectTransform panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.sizeDelta = new Vector2(-6, -6); // Thụt lề 3px xung quanh để lộ viền Indigo
            panelRect.anchoredPosition = Vector2.zero;

            RectTransform innerTailRect = innerTailGo.GetComponent<RectTransform>();
            innerTailRect.sizeDelta = new Vector2(16, 16);
            innerTailRect.anchoredPosition = new Vector2(0, -52f); // Nhích lên 3px để khớp viền
            innerTailRect.localRotation = Quaternion.Euler(0, 0, 45);

            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = new Vector2(-24, -20); // Padding cho text tránh sát lề
            textRect.anchoredPosition = Vector2.zero;

            // Hiệu ứng luôn hướng về phía Camera người chơi (Billboard)
            canvasGo.AddComponent<BillboardEffect>();

            _activeBubble = canvasGo;
            Destroy(canvasGo, duration);
        }

        private void PrepareRoundedSprites()
        {
            if (_roundedBorderSprite != null) return;

            int size = 64;
            int radius = 16;
            Color borderCol = new Color(0.39f, 0.35f, 0.96f, 1.0f);

            // 1. Border sprite (Indigo)
            Texture2D borderTex = GenerateRoundedTexture(size, size, radius, borderCol);
            _roundedBorderSprite = Sprite.Create(borderTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.Tight, new Vector4(radius, radius, radius, radius));

            // 2. Background sprite (White)
            Texture2D bgTex = GenerateRoundedTexture(size, size, radius, Color.white);
            _roundedBackgroundSprite = Sprite.Create(bgTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.Tight, new Vector4(radius, radius, radius, radius));

            // 3. Tail sprite (Triangle / Diamond)
            Texture2D solidIndigoTex = CreateSolidTexture(16, 16, borderCol);
            _tailSprite = Sprite.Create(solidIndigoTex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f));

            // 4. Inner Tail sprite (White)
            Texture2D solidWhiteTex = CreateSolidTexture(16, 16, Color.white);
            _innerTailSprite = Sprite.Create(solidWhiteTex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f));
        }

        private Texture2D GenerateRoundedTexture(int width, int height, int radius, Color color)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] colors = new Color[width * height];
            
            float r2 = radius * radius;
            
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bool isInside = true;
                    
                    // Bottom-left corner
                    if (x < radius && y < radius)
                    {
                        float dx = x - radius;
                        float dy = y - radius;
                        isInside = (dx * dx + dy * dy) <= r2;
                    }
                    // Top-left corner
                    else if (x < radius && y >= height - radius)
                    {
                        float dx = x - radius;
                        float dy = y - (height - 1 - radius);
                        isInside = (dx * dx + dy * dy) <= r2;
                    }
                    // Bottom-right corner
                    else if (x >= width - radius && y < radius)
                    {
                        float dx = x - (width - 1 - radius);
                        float dy = y - radius;
                        isInside = (dx * dx + dy * dy) <= r2;
                    }
                    // Top-right corner
                    else if (x >= width - radius && y >= height - radius)
                    {
                        float dx = x - (width - 1 - radius);
                        float dy = y - (height - 1 - radius);
                        isInside = (dx * dx + dy * dy) <= r2;
                    }

                    colors[y * width + x] = isInside ? color : Color.clear;
                }
            }
            
            tex.SetPixels(colors);
            tex.Apply();
            return tex;
        }

        private Texture2D CreateSolidTexture(int width, int height, Color color)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] colors = new Color[width * height];
            for (int i = 0; i < colors.Length; i++) colors[i] = color;
            tex.SetPixels(colors);
            tex.Apply();
            return tex;
        }
    } 

    public class BillboardEffect : MonoBehaviour
    {
        private void Update()
        {
            if (Camera.main != null)
            {
                transform.LookAt(transform.position + Camera.main.transform.rotation * Vector3.forward,
                    Camera.main.transform.rotation * Vector3.up);
            }
        }
    }
}
