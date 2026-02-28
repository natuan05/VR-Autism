using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HuggingFace.API;

public class VoiceProcessor : MonoBehaviour
{
    public bool IsRecording => _audioClip != null && Microphone.IsRecording(CurrentDeviceName);

    [SerializeField] private int MicrophoneIndex;
    public int SampleRate { get; private set; }
    public int FrameLength { get; private set; }

    public event Action OnRecordingStop;
    public event Action OnRecordingStart;

    private List<string> Devices;
    private int CurrentDeviceIndex;
    private string CurrentDeviceName => (CurrentDeviceIndex >= 0 && CurrentDeviceIndex < Microphone.devices.Length) ? Devices[CurrentDeviceIndex] : string.Empty;

    [Header("Voice Detection Settings")]
    [SerializeField] private float _minimumSpeakingSampleValue = 0.02f; 
    [SerializeField] private float _silenceTimer = 1.0f; 

    private float _timeAtSilenceBegan;
    private bool _audioDetected;
    private bool _isSpeaking;

    private AudioClip _audioClip;

    void Awake()
    {
        UpdateDevices();
        StartRecording();
    }

    public void UpdateDevices()
    {
        Devices = new List<string>(Microphone.devices);
        CurrentDeviceIndex = Devices.Count > 0 ? MicrophoneIndex : -1;
    }

    public void StartRecording(int sampleRate = 16000, int frameSize = 512)
    {
        if (IsRecording) return;

        SampleRate = sampleRate;
        FrameLength = frameSize;
        _audioClip = Microphone.Start(CurrentDeviceName, true, 1, sampleRate);

        StartCoroutine(DetectVoice());
    }

    public void StopRecording()
    {
        if (!IsRecording) return;

        Microphone.End(CurrentDeviceName);
        StopCoroutine(DetectVoice());
        StartCoroutine(ProcessAudio());

        if (IsRecording)
        {
            StartCoroutine(DetectVoice());
        }
        else
        {
            Destroy(_audioClip);
            _audioClip = null;
            _isSpeaking = false;
        }
    }

    IEnumerator DetectVoice()
    {
        float[] sampleBuffer = new float[FrameLength];
        int startReadPos = 0;

        OnRecordingStart?.Invoke();

        while (IsRecording)
        {
            int curClipPos = Microphone.GetPosition(CurrentDeviceName);
            if (curClipPos < startReadPos) curClipPos += _audioClip.samples;
            
            int samplesAvailable = curClipPos - startReadPos;
            if (samplesAvailable < FrameLength) yield return null;
            
            int endReadPos = startReadPos + FrameLength;
            _audioClip.GetData(sampleBuffer, startReadPos);
            startReadPos = endReadPos % _audioClip.samples;

            float maxVolume = 0.0f;
            foreach (var sample in sampleBuffer) maxVolume = Math.Max(maxVolume, sample);

            if (maxVolume >= _minimumSpeakingSampleValue)
            {
                if (!_isSpeaking)
                {
                    _isSpeaking = true;
                    Debug.Log("🎤 Phát hiện giọng nói...");
                }
                _timeAtSilenceBegan = Time.time;
            }
            else
            {
                if (_isSpeaking && Time.time - _timeAtSilenceBegan > _silenceTimer)
                {
                    _isSpeaking = false;
                    Debug.Log("🔇 Người dùng đã dừng nói.");
                    StopRecording();
                }
            }
        }

        yield return null;
    }

    private IEnumerator ProcessAudio()
    {
        Debug.Log("🛠 Xử lý giọng nói...");
        
        float[] samples = new float[_audioClip.samples * _audioClip.channels];
        _audioClip.GetData(samples, 0);

        // byte[] audioBytes = new byte[samples.Length * sizeof(float)];
        // Buffer.BlockCopy(samples, 0, audioBytes, 0, audioBytes.Length);
        var audioBytes = EncodeAsWAV(samples, _audioClip.frequency, _audioClip.channels);

        HuggingFaceAPI.AutomaticSpeechRecognition(audioBytes, result => {
            Debug.Log("📜 Văn bản chuyển đổi: " + result);

            ExtractInfo(result);
        }, error => {
            Debug.LogError(error);
           
        });
        yield return null;
    }
    
    private byte[] EncodeAsWAV(float[] samples, int frequency, int channels) {
        using (var memoryStream = new MemoryStream(44 + samples.Length * 2)) {
            using (var writer = new BinaryWriter(memoryStream)) {
                writer.Write("RIFF".ToCharArray());
                writer.Write(36 + samples.Length * 2);
                writer.Write("WAVE".ToCharArray());
                writer.Write("fmt ".ToCharArray());
                writer.Write(16);
                writer.Write((ushort)1);
                writer.Write((ushort)channels);
                writer.Write(frequency);
                writer.Write(frequency * channels * 2);
                writer.Write((ushort)(channels * 2));
                writer.Write((ushort)16);
                writer.Write("data".ToCharArray());
                writer.Write(samples.Length * 2);
                foreach (var sample in samples) {
                    writer.Write((short)(sample * short.MaxValue));
                }
            }
            return memoryStream.ToArray();
        }
    }

    private void ExtractInfo(string text)
    {
        string namePattern = @"tên tôi là ([\w\s]+)";
        string agePattern = @"(\d{1,2}) tuổi";

        Match nameMatch = Regex.Match(text, namePattern, RegexOptions.IgnoreCase);
        Match ageMatch = Regex.Match(text, agePattern, RegexOptions.IgnoreCase);

        if (nameMatch.Success)
        {
            Debug.Log("📛 Tên: " + nameMatch.Groups[1].Value);
        }

        if (ageMatch.Success)
        {
            Debug.Log("🎂 Tuổi: " + ageMatch.Groups[1].Value);
        }

        if (!nameMatch.Success && !ageMatch.Success)
        {
            Debug.Log("⚠️ Không tìm thấy thông tin tên hoặc tuổi.");
        }
    }
}
