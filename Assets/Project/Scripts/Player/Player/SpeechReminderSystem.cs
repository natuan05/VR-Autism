using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SpeechReminderSystem : MonoBehaviour
{
    public TextMeshProUGUI reminderText;
    public float checkInterval = 30f; // Thời gian kiểm tra (30 giây)
    private float timer;
    private bool hasSpokenName = false;
    private bool hasSpokenAge = false;
    private bool hasSpokenHobby = false;

    void Start()
    {
        timer = checkInterval;
        StartCoroutine(CheckSpeech());
    }

    void Update()
    {
        timer -= Time.deltaTime;
        if (timer <= 0)
        {
            GiveReminder();
            timer = checkInterval; // Reset bộ đếm
        }
    }

    IEnumerator CheckSpeech()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);
            string recognizedSpeech = GetRecognizedSpeech(); // Hàm này cần tích hợp API Speech-to-Text
            ProcessSpeech(recognizedSpeech);
        }
    }

    void ProcessSpeech(string speech)
    {
        if (string.IsNullOrEmpty(speech)) return;

        if (!hasSpokenName && (speech.Contains("mình tên là") || speech.Contains("tôi tên là")))
        {
            hasSpokenName = true;
        }
        if (!hasSpokenAge && speech.Contains("tuổi"))
        {
            hasSpokenAge = true;
        }
        if (!hasSpokenHobby && (speech.Contains("thích") || speech.Contains("sở thích")))
        {
            hasSpokenHobby = true;
        }
    }

    void GiveReminder()
    {
        if (!hasSpokenName)
        {
            reminderText.text = "Con có thể bắt đầu bằng cách nói: ‘Mình tên là…’";
        }
        else if (!hasSpokenAge)
        {
            reminderText.text = "Giờ hãy nói tuổi của con: ‘Mình … tuổi.’";
        }
        else if (!hasSpokenHobby)
        {
            reminderText.text = "Con thích gì? Hãy nói: ‘Sở thích của mình là…’";
        }
        else
        {
            reminderText.text = "Con đã giới thiệu xong! Rất tốt!";
        }
    }

    string GetRecognizedSpeech()
    {
        // Ở đây bạn cần tích hợp API nhận diện giọng nói (Google, Azure,...)
        // Tạm thời giả lập với chuỗi rỗng
        return "";
    }
}
