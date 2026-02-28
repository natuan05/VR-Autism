using System;
using System.IO;
using HuggingFace.API;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Threading.Tasks;

public class SpeechRecognition : MonoBehaviour {
    [SerializeField] private SpeechResponser speechResponser;
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private int clipLengthCycle = 10;
    [SerializeField] private float checkInterval = 5f;
    private AudioClip clip;
    private byte[] bytes;
    private bool recording;

    private void Start()
    {
        string[] devices = Microphone.devices;
        Debug.Log("Available Microphone Devices:");
        foreach (string device in devices)
        {
            Debug.Log("Mic: " + device);
        }
    }

    public void StartRecording() {
        text.color = Color.white;
        text.text = "Listening...";
        clip = Microphone.Start(null, true, clipLengthCycle, 44100);
        recording = true;
        recording = true;
        lastSamplePosition = 0;
        StartCoroutine(CaptureAudioLoop());
    }
    
    private int lastSamplePosition = 0;
    private const int sampleRate = 44100;
    private const int bufferSeconds = 10;

    private IEnumerator CaptureAudioLoop() {
        while (recording) {
            yield return new WaitForSeconds(checkInterval);

            int currentPosition = Microphone.GetPosition(null);
            int samplesToRead = currentPosition - lastSamplePosition;
            if (samplesToRead < 0) {
                // Đã loop buffer
                samplesToRead += bufferSeconds * sampleRate;
            }

            float[] samples = new float[samplesToRead];
            clip.GetData(samples, lastSamplePosition);

            lastSamplePosition = currentPosition;

            byte[] encoded = EncodeAsWAV(samples, sampleRate, clip.channels);
            StartCoroutine(SendRecordingCoroutine(encoded)); // async
        }
    }

    public void StopRecording()
    {
        recording = false;
        Microphone.End(null);
    }
    
    private IEnumerator SendRecordingCoroutine(Byte[] bytess) {
        text.color = Color.yellow;
        text.text = "Processing...";

        bool done = false;
        string responseText = "";
        string errorText = "";

        HuggingFaceAPI.AutomaticSpeechRecognition(bytess, response => {
            responseText = response;
            done = true;
        }, error => {
            errorText = error;
            done = true;
        });

        while (!done) {
            yield return null; // đợi frame sau
        }

        if (!string.IsNullOrEmpty(errorText)) {
            Debug.LogError(errorText);
            text.color = Color.red;
            text.text = errorText;
        } else {
            text.color = Color.white;
            text.text = responseText;
            AnalyzeSpeech(responseText);
        }
    }

    private void AnalyzeSpeech(string speech) {
        if (string.IsNullOrEmpty(speech))
        {
            speech = "";
        }

        if (speechResponser != null)
        {
            speechResponser.AnalyzeSpeech(speech);
        }
        
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

    public void SetSpeechResponser(SpeechResponser newSpeechResponser)
    {
        speechResponser = newSpeechResponser;
    }
}
