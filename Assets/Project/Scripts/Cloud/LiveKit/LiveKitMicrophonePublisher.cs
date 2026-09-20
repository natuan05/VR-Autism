using System;
using System.Threading.Tasks;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;

namespace VRAutism.Cloud.LiveKit
{
    internal interface ILiveKitMicrophonePublication : IDisposable
    {
        Task PublishAsync(RoomConnectionHandle handle);
        void Start();
        void SetMuted(bool muted);
        void Unpublish(RoomConnectionHandle handle);
    }

    internal interface ILiveKitMicrophonePublicationFactory
    {
        bool TryCreate(Transform parent, out ILiveKitMicrophonePublication publication);
    }

    internal sealed class LiveKitMicrophonePublisher
    {
        private readonly ILiveKitMicrophonePublicationFactory _factory;
        private readonly Transform _parent;
        private ILiveKitMicrophonePublication _activePublication;
        private RoomConnectionHandle _activeHandle;

        internal LiveKitMicrophonePublisher(
            ILiveKitMicrophonePublicationFactory factory,
            Transform parent)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _parent = parent;
        }

        internal async Task SetEnabledAsync(
            bool enable,
            RoomConnectionHandle handle,
            Func<long, bool> isGenerationCurrent)
        {
            if (!enable)
            {
                if (_activePublication != null)
                    _activePublication.SetMuted(true);
                return;
            }

            if (_activePublication != null)
            {
                _activePublication.SetMuted(false);
                return;
            }

            if (handle == null || !handle.IsConnected)
                return;

            if (!_factory.TryCreate(_parent, out var candidate) || candidate == null)
                return;

            var capturedHandle = handle;
            var capturedGeneration = capturedHandle.Generation;
            try
            {
                await candidate.PublishAsync(capturedHandle);

                if (isGenerationCurrent == null || !isGenerationCurrent(capturedGeneration))
                {
                    Rollback(candidate, capturedHandle);
                    return;
                }

                candidate.Start();
                candidate.SetMuted(false);
                _activePublication = candidate;
                _activeHandle = capturedHandle;
            }
            catch (Exception exception)
            {
                Rollback(candidate, capturedHandle);
                Debug.LogError($"[LiveKitService] ❌ Lỗi xử lý Mic: {exception.Message}");
            }
        }

        internal void Stop()
        {
            var publication = _activePublication;
            var handle = _activeHandle;
            _activePublication = null;
            _activeHandle = null;

            if (publication == null)
                return;

            Rollback(publication, handle);
        }

        private static void Rollback(
            ILiveKitMicrophonePublication publication,
            RoomConnectionHandle handle)
        {
            try
            {
                publication.Unpublish(handle);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LiveKitService] Microphone unpublish cleanup notice: {exception.Message}");
            }

            try
            {
                publication.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LiveKitService] Microphone dispose cleanup notice: {exception.Message}");
            }
        }
    }

    internal sealed class LiveKitMicrophonePublicationFactory : ILiveKitMicrophonePublicationFactory
    {
        public bool TryCreate(Transform parent, out ILiveKitMicrophonePublication publication)
        {
            publication = null;
            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Debug.LogError("[LiveKitService] ❌ Không tìm thấy thiết bị Microphone nào trên máy!");
                return false;
            }

            var microphoneDevice = Microphone.devices[0];
            Debug.Log($"[LiveKitService] 🎙️ Tìm thấy Mic phần cứng: '{microphoneDevice}'. Đang khởi tạo luồng...");

            var micGameObject = new GameObject($"LiveKitMic_{microphoneDevice}");
            micGameObject.transform.SetParent(parent);
            var micSource = new MicrophoneSource(microphoneDevice, micGameObject);
            publication = new LiveKitMicrophonePublication(micGameObject, micSource);
            return true;
        }
    }

    internal sealed class LiveKitMicrophonePublication : ILiveKitMicrophonePublication
    {
        private readonly GameObject _micGameObject;
        private readonly MicrophoneSource _micSource;
        private LocalAudioTrack _localAudioTrack;
        private bool _disposed;

        internal LiveKitMicrophonePublication(GameObject micGameObject, MicrophoneSource micSource)
        {
            _micGameObject = micGameObject ?? throw new ArgumentNullException(nameof(micGameObject));
            _micSource = micSource ?? throw new ArgumentNullException(nameof(micSource));
        }

        public async Task PublishAsync(RoomConnectionHandle handle)
        {
            _localAudioTrack = LocalAudioTrack.CreateAudioTrack("microphone", _micSource, handle.SdkRoom);
            var options = new TrackPublishOptions
            {
                AudioEncoding = new AudioEncoding { MaxBitrate = 64000 },
                Source = TrackSource.SourceMicrophone
            };

            await handle.Adapter.PublishAudioTrackAsync(_localAudioTrack, options);
            Debug.Log("[LiveKitService] 🎙️ Đã Publish luồng Microphone lên LiveKit Server thành công!");
        }

        public void Start()
        {
            _micSource.Start();
        }

        public void SetMuted(bool muted)
        {
            if (_localAudioTrack != null)
                ((ILocalTrack)_localAudioTrack).SetMute(muted);

            Debug.Log(muted
                ? "[LiveKitService] 🎙️ MICROPHONE ĐÃ TẮT & MUTE"
                : "[LiveKitService] 🎙️ MICROPHONE ĐANG BẬT & UNMUTE (Đang thu âm)");
        }

        public void Unpublish(RoomConnectionHandle handle)
        {
            if (_localAudioTrack != null && handle != null)
                handle.Adapter.UnpublishAudioTrack(_localAudioTrack);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _micSource.Dispose();
            if (_micGameObject != null)
                UnityEngine.Object.Destroy(_micGameObject);
            _localAudioTrack = null;
        }
    }
}
