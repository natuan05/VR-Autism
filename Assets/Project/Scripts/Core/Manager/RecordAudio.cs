using System.Collections;
using System.Collections.Generic;
using System.IO;
using VRAutism.Core;
using UnityEngine;

namespace VRAutism.Core
{
    public class RecordAudio : MonoBehaviour
    {
        private AudioClip recordedClip;
        private string filePath = "recording.wav";
        private string directoryPath = "Recordings";
        private float startTime;
        private float recordingLength;

        private void Awake()
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        public void StartRecording()
        {
            string device = Microphone.devices[0];
            int sampleRate = 44100;
            int lengthSec = 3599;

            recordedClip = Microphone.Start(device, false, lengthSec, sampleRate);
            startTime = Time.realtimeSinceStartup;
        }

        public void StopRecording()
        {
            Microphone.End(null);
            recordingLength = Time.realtimeSinceStartup - startTime;
            recordedClip = TrimClip(recordedClip, recordingLength);
            SaveRecording();
        }

        public void SaveRecording()
        {
            if (recordedClip != null)
            {
                filePath = Path.Combine(directoryPath, filePath);
                WavUtils.Save(filePath, recordedClip);
                Debug.Log("Recording saved as " + filePath);
            }
            else
            {
                Debug.LogError("No recording found to save.");
            }
        }

        private AudioClip TrimClip(AudioClip clip, float length)
        {
            int samples = (int)(clip.frequency * length);
            float[] data = new float[samples];
            clip.GetData(data, 0);

            AudioClip trimmedClip = AudioClip.Create(clip.name, samples,
                clip.channels, clip.frequency, false);
            trimmedClip.SetData(data, 0);

            return trimmedClip;
        }
    }
}