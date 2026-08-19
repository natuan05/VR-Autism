using UnityEngine;
using UnityEngine.Events;
using VRAutism.Cloud.LiveKit;

namespace VRAutism.Gameplay.Actions
{
    public class VoiceQuest : Quest
    {
        [Header("Voice AI Setup")]
        [SerializeField] private string[] defaultPhrases;
        [SerializeField] private AudioSource npcAudioSource;

        [Header("Reminder Setup")]
        [SerializeField] private UnityEvent onQuestReminder;

        public string[] DefaultPhrases => defaultPhrases;

        /// <summary>Lấy danh sách phrases động từ SessionContext (ưu tiên), hoặc fallback về Inspector defaultPhrases</summary>
        public string[] GetActivePhrases()
        {
            string[] ctxPhrases = VRAutism.Core.SessionContext.Instance?.GetPhrasesByQuestIndex(Id);
            if (ctxPhrases != null && ctxPhrases.Length > 0)
            {
                return ctxPhrases;
            }
            return defaultPhrases;
        }

        protected override void OnBegin()
        {
            base.OnBegin();
            string nameToSend = Name;
            string[] phrasesToSend = GetActivePhrases();
            int phraseCount = phrasesToSend != null ? phrasesToSend.Length : 0;
            
            Debug.Log($"[VoiceQuest] 🚀 1. Kích hoạt VoiceQuest: '{nameToSend}' (Id: {Id}) | Số câu mẫu: {phraseCount}");
            
            if (LiveKitService.Instance != null)
            {
                if (npcAudioSource != null)
                {
                    Debug.Log($"[VoiceQuest] 🔊 2. Đã gán AudioSource của NPC '{npcAudioSource.gameObject.name}' cho LiveKit");
                    LiveKitService.Instance.SetAudioSource(npcAudioSource);
                }
                else
                {
                    Debug.LogWarning("[VoiceQuest] ⚠️ Chưa kéo thả AudioSource của NPC vào Inspector!");
                }

                Debug.Log("[VoiceQuest] 🎤 3. Yêu cầu LiveKitService bật Microphone & Đăng ký nhận kết quả...");
                LiveKitService.Instance.EnableMicrophone(true);
                LiveKitService.Instance.OnSpeechMatched += HandleSpeechMatched;
                LiveKitService.Instance.SendActiveQuest(nameToSend, phrasesToSend);
            }
            else
            {
                Debug.LogError("[VoiceQuest] ❌ LỖI: LiveKitService.Instance bằng NULL! Hãy kiểm tra GameObject LiveKitService trong Scene.");
            }
        }

        protected override void OnEnd()
        {
            Debug.Log($"[VoiceQuest] 🛑 Kết thúc VoiceQuest: '{Name}' -> Tắt Mic & Hủy Đăng ký");
            if (LiveKitService.Instance != null)
            {
                LiveKitService.Instance.OnSpeechMatched -= HandleSpeechMatched;
                LiveKitService.Instance.EnableMicrophone(false);
            }
            base.OnEnd();
        }

        /// <summary>
        /// Kích hoạt gợi ý lời nói (Verbal Hint / Reminder) cho Quest hiện tại.
        /// </summary>
        public override void OnVerbalHint()
        {
            Debug.Log($"[VoiceQuest] 🔊 Kích hoạt Verbal Hint cho Quest '{Name}' (Id: {Id})");

            // 1. Kích hoạt UnityEvent nếu có cấu hình
            onQuestReminder?.Invoke();

            // 2. Gửi tín hiệu VERBAL_HINT tới LiveKit Agent để phát câu gợi ý từ cache
            if (LiveKitService.Instance != null)
            {
                LiveKitService.Instance.SendVerbalHint();
            }
        }

        private void HandleSpeechMatched()
        {
            Debug.Log($"[VoiceQuest] 🎉 NHẬN TÍN HIỆU THÀNH CÔNG từ AI! Trẻ đọc đúng Quest '{Name}' ➔ Hoàn thành Quest!");
            Controller?.CompleteActiveQuest();
        }
    }   
}