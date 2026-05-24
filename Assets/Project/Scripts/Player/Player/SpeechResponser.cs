using System;
using System.Collections;
using VRAutism.Core;
using VRAutism.Core.Models;
using UnityEngine;
using UnityEngine.Events;

public class SpeechResponser : MonoBehaviour
{
    [SerializeField] private float timeBeforePrompt = 5f; // Fallback khi SessionContext chưa sẵn sàng
    [SerializeField] private BooleanVariable finishCondition;
    [SerializeField] private IntVariable hintCount;
    [SerializeField] private SpeechCategory[] speechCategories; // Mảng chứa các chủ đề

    /// <summary>
    /// Thời gian lặng động: ưu tiên SessionContext.CurrentParams.SpeechSilenceTimeout,
    /// fallback về Inspector field nếu SessionContext chưa khởi tạo hoặc giá trị là sentinel (-1f).
    /// Patch 6: Clamp Mathf.Max(0f, ...) để bảo vệ WaitForSeconds khỏi nhận giá trị âm.
    /// </summary>
    private float EffectiveSilenceTimeout
    {
        get
        {
            if (SessionContext.Instance != null)
            {
                float configuredTimeout = SessionContext.Instance.CurrentParams.Actions.SpeechSilenceTimeout;
                // Chỉ dùng giá trị config khi >= 0f (hợp lệ từ Firestore); -1f là sentinel
                if (configuredTimeout >= 0f)
                    return Mathf.Max(0f, configuredTimeout);
            }
            return Mathf.Max(0f, timeBeforePrompt);
        }
    }
    
    public Action<AudioClip> OnPrompt;
    private Coroutine silenceTimer;

    public void StartResponse()
    {
        ResetSilenceTimer();
        finishCondition.Value = false;
        stop = false;
    }

    public void AnalyzeSpeech(string text)
    {
        text = text.ToLower();
        bool found = false;

        foreach (var category in speechCategories)
        {
            var words = category.key.Split(',');
            foreach (var word in words)
            {
                if (text.Contains(word))
                {
                    category.hasSpoken = true;
                    found = true;
                    break;
                }
            }
        }

        if (found)
        {
            if (!CheckFinish())
            {
                ResetSilenceTimer();
            }
            else
            {
                finishCondition.Value = true;
                Debug.LogError("<color=green>Finish Response</color>");
            }
        }


        Debug.LogError(text);
    }

    public bool CheckFinish()
    {
        foreach (var category in speechCategories)
        {
            if (!category.hasSpoken) return false;
        }
        return true;
    }

    private void ResetSilenceTimer()
    {
        if (silenceTimer != null)
        {
            StopCoroutine(silenceTimer);
        }
        silenceTimer = StartCoroutine(SilenceCountdown());
    }

    bool stop = false;
    public void StopResponse()
    {
        stop = true;
        if (silenceTimer != null) StopCoroutine(silenceTimer);
    }

    private IEnumerator SilenceCountdown()
    {
        yield return new WaitForSeconds(EffectiveSilenceTimeout);
        if (stop) yield break;
        PromptTeacher();
    }

    private void PromptTeacher()
    {
        // string prompt = GetPrompt();
        OnPrompt?.Invoke(GetPrompt());
        hintCount.Value++;
        Debug.LogError("<color=yellow>Nhận gợi ý</color>");
        ResetSilenceTimer();
    }

    private AudioClip GetPrompt()
    {
        foreach (var category in speechCategories)
        {
            if (!category.hasSpoken)
                return category.GetRandomAudio();
        }
        
        return null;
        // return "Con có thể kể cho cô nghe một điều thú vị về mình không?";
    }
}

[Serializable]
public class SpeechCategory
{
    public string key;               // Từ khóa cần nhận diện
    // public string[] prompts; // Mảng câu hỏi gợi ý
    public AudioClip[] audios;
    public bool hasSpoken = false;   // Kiểm tra xem trẻ đã nói về chủ đề này chưa

    // Lấy một câu gợi ý ngẫu nhiên từ mảng
    // public string GetRandomPrompt()
    // {
    //     if (prompts.Length > 0)
    //     {
    //         return prompts[UnityEngine.Random.Range(0, prompts.Length)];
    //     }
    //     return "Hãy chia sẻ thêm về con nào!";
    // }

    public AudioClip GetRandomAudio()
    {
        if (audios.Length > 0)
        {
            return audios[UnityEngine.Random.Range(0, audios.Length)];
        }
        return null;
    }
}
