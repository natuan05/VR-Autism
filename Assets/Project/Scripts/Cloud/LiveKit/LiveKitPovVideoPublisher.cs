using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using LiveKit;
using LiveKit.Proto;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VRAutism.Cloud.LiveKit
{
    internal interface ILiveKitPovPublication : IDisposable
    {
        Task PublishAsync(RoomConnectionHandle handle);
        void BeginFrames();
        void Unpublish(RoomConnectionHandle handle);
    }

    internal interface ILiveKitPovPublicationFactory
    {
        ILiveKitPovPublication Create(Camera camera, int width, int height, int frameRate);
    }

    internal sealed class LiveKitPovVideoPublisher
    {
        private const int ConnectionWaitAttempts = 50;
        private const int ConnectionWaitMilliseconds = 200;

        private readonly ILiveKitPovPublicationFactory _factory;
        private readonly ILiveKitCoroutineHost _coroutineHost;
        private readonly int _width;
        private readonly int _height;
        private readonly int _frameRate;
        private ILiveKitPovPublication _activePublication;
        private RoomConnectionHandle _activeHandle;
        private PendingPublication _pendingPublication;
        private CancellationTokenSource _enableCancellation;
        private long _operationEpoch;

        internal LiveKitPovVideoPublisher(
            ILiveKitPovPublicationFactory factory,
            ILiveKitCoroutineHost coroutineHost,
            int width = 1280,
            int height = 720,
            int frameRate = 30)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _coroutineHost = coroutineHost ?? throw new ArgumentNullException(nameof(coroutineHost));
            _width = width;
            _height = height;
            _frameRate = frameRate;
        }

        internal async Task EnableAsync(
            Camera camera,
            Func<RoomConnectionHandle> currentHandleProvider,
            Func<long, bool> isGenerationCurrent,
            CancellationToken cancellationToken)
        {
            var operation = BeginOperation(cancellationToken);
            try
            {
                await EnableCoreAsync(camera, currentHandleProvider, isGenerationCurrent, operation);
            }
            finally
            {
                EndOperation(operation);
            }
        }

        private async Task EnableCoreAsync(
            Camera camera,
            Func<RoomConnectionHandle> currentHandleProvider,
            Func<long, bool> isGenerationCurrent,
            Operation operation)
        {
            if (camera == null)
                camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();

            if (camera == null)
            {
                Debug.LogWarning("[LiveKitService] EnablePOVCamera: vrCamera is null and no Camera found in scene!");
                return;
            }

            if (currentHandleProvider == null)
                return;

            if (!IsOperationCurrent(operation))
                return;

            var currentHandle = currentHandleProvider();
            var waitCount = 0;
            while ((currentHandle == null || !currentHandle.IsConnected) && waitCount < ConnectionWaitAttempts)
            {
                var observedGeneration = currentHandle?.Generation;
                try
                {
                    await Task.Delay(ConnectionWaitMilliseconds, operation.Token);
                }
                catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
                {
                    return;
                }

                if (!IsOperationCurrent(operation))
                    return;

                if (observedGeneration.HasValue &&
                    isGenerationCurrent != null &&
                    !isGenerationCurrent(observedGeneration.Value))
                    return;

                currentHandle = currentHandleProvider();
                waitCount++;
            }

            if (currentHandle == null || !currentHandle.IsConnected)
            {
                Debug.LogWarning("[LiveKitService] ⚠️ Không thể bật POV Camera: Room chưa kết nối!");
                return;
            }

            if (!IsOperationCurrent(operation) ||
                (isGenerationCurrent != null && !isGenerationCurrent(currentHandle.Generation)))
                return;

            if (_activePublication != null || _pendingPublication != null)
            {
                Debug.Log("[LiveKitService] POV Camera is already streaming.");
                return;
            }

            var capturedHandle = currentHandle;
            ILiveKitPovPublication candidate = null;
            PendingPublication pending = null;
            try
            {
                Debug.Log($"[LiveKitService] 📹 Khởi tạo POV Video Stream ({_width}x{_height} @ {_frameRate}fps)...");
                candidate = _factory.Create(camera, _width, _height, _frameRate);
                if (candidate == null)
                    return;

                pending = new PendingPublication(candidate, capturedHandle);
                _pendingPublication = pending;

                await candidate.PublishAsync(capturedHandle);

                if (!ReferenceEquals(_pendingPublication, pending) || pending.Cleaned)
                    return;

                if (!IsOperationCurrent(operation) ||
                    (isGenerationCurrent != null && !isGenerationCurrent(capturedHandle.Generation)))
                {
                    Cleanup(pending);
                    _pendingPublication = null;
                    return;
                }

                candidate.BeginFrames();
                _activePublication = candidate;
                _activeHandle = capturedHandle;
                _pendingPublication = null;
                Debug.Log("[LiveKitService] ✅ POV Video Track published thành công với luồng frame hoạt động!");
            }
            catch (OperationCanceledException)
            {
                if (pending != null && !pending.Cleaned && ReferenceEquals(_pendingPublication, pending))
                {
                    Cleanup(pending);
                    _pendingPublication = null;
                }
                return;
            }
            catch (Exception exception)
            {
                if (pending != null && !pending.Cleaned && ReferenceEquals(_pendingPublication, pending))
                {
                    Cleanup(pending);
                    _pendingPublication = null;
                }

                Debug.LogError($"[LiveKitService] ❌ Lỗi khởi tạo POV Camera: {exception.Message}");
            }
        }

        private Operation BeginOperation(CancellationToken cancellationToken)
        {
            _enableCancellation?.Cancel();
            _enableCancellation?.Dispose();

            var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var operation = new Operation(++_operationEpoch, linkedCancellation);
            _enableCancellation = linkedCancellation;
            return operation;
        }

        private bool IsOperationCurrent(Operation operation) =>
            ReferenceEquals(_enableCancellation, operation.Cancellation) &&
            _operationEpoch == operation.Epoch &&
            !operation.Token.IsCancellationRequested;

        private void EndOperation(Operation operation)
        {
            if (ReferenceEquals(_enableCancellation, operation.Cancellation))
                _enableCancellation = null;

            operation.Cancellation.Dispose();
        }

        internal void Disable()
        {
            _operationEpoch++;
            _enableCancellation?.Cancel();
            _enableCancellation?.Dispose();
            _enableCancellation = null;

            var pending = _pendingPublication;
            _pendingPublication = null;
            var hadPublication = pending != null || _activePublication != null;
            if (pending != null)
                Cleanup(pending);

            var active = _activePublication;
            var activeHandle = _activeHandle;
            _activePublication = null;
            _activeHandle = null;
            if (active != null)
                Rollback(active, activeHandle);

            if (hadPublication)
                Debug.Log("[LiveKitService] 🛑 Đã tắt POV Video Stream");
        }

        private sealed class PendingPublication
        {
            internal readonly ILiveKitPovPublication Publication;
            internal readonly RoomConnectionHandle Handle;
            internal bool Cleaned;

            internal PendingPublication(ILiveKitPovPublication publication, RoomConnectionHandle handle)
            {
                Publication = publication;
                Handle = handle;
            }
        }

        private sealed class Operation
        {
            internal readonly long Epoch;
            internal readonly CancellationTokenSource Cancellation;
            internal CancellationToken Token => Cancellation.Token;

            internal Operation(long epoch, CancellationTokenSource cancellation)
            {
                Epoch = epoch;
                Cancellation = cancellation;
            }
        }

        private static void Cleanup(PendingPublication pending)
        {
            if (pending.Cleaned)
                return;

            pending.Cleaned = true;
            Rollback(pending.Publication, pending.Handle);
        }

        private static void Rollback(ILiveKitPovPublication publication, RoomConnectionHandle handle)
        {
            try
            {
                publication.Unpublish(handle);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LiveKitService] POV unpublish cleanup notice: {exception.Message}");
            }

            try
            {
                publication.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LiveKitService] POV dispose cleanup notice: {exception.Message}");
            }
        }
    }

    internal sealed class LiveKitPovPublicationFactory : ILiveKitPovPublicationFactory
    {
        private readonly ILiveKitCoroutineHost _coroutineHost;

        internal LiveKitPovPublicationFactory(ILiveKitCoroutineHost coroutineHost)
        {
            _coroutineHost = coroutineHost ?? throw new ArgumentNullException(nameof(coroutineHost));
        }

        public ILiveKitPovPublication Create(Camera camera, int width, int height, int frameRate) =>
            new LiveKitPovPublication(_coroutineHost, camera, width, height, frameRate);
    }

    internal sealed class LiveKitPovPublication : ILiveKitPovPublication
    {
        private readonly ILiveKitCoroutineHost _coroutineHost;
        private Camera _captureCamera;
        private GameObject _captureObject;
        private RenderTexture _renderTexture;
        private TextureVideoSource _videoSource;
        private readonly int _frameRate;
        private LocalVideoTrack _localVideoTrack;
        private Coroutine _videoSourceCoroutine;
        private RoomConnectionHandle _publishedHandle;
        private bool _unpublished;
        private bool _disposed;

        internal LiveKitPovPublication(
            ILiveKitCoroutineHost coroutineHost,
            Camera sourceCamera,
            int width,
            int height,
            int frameRate)
        {
            _coroutineHost = coroutineHost ?? throw new ArgumentNullException(nameof(coroutineHost));
            if (sourceCamera == null)
                throw new ArgumentNullException(nameof(sourceCamera));
            _frameRate = frameRate;

            try
            {
                _captureObject = new GameObject("LiveKit_POVCaptureCamera");
                _captureObject.transform.SetParent(sourceCamera.transform, false);
                _captureObject.transform.localPosition = Vector3.zero;
                _captureObject.transform.localRotation = Quaternion.identity;

                _captureCamera = _captureObject.AddComponent<Camera>();
                _captureCamera.CopyFrom(sourceCamera);
                _captureCamera.cullingMask = sourceCamera.cullingMask;
                _captureCamera.clearFlags = sourceCamera.clearFlags;
                _captureCamera.backgroundColor = sourceCamera.backgroundColor;
                _captureCamera.fieldOfView = sourceCamera.fieldOfView;
                _captureCamera.nearClipPlane = sourceCamera.nearClipPlane;
                _captureCamera.farClipPlane = sourceCamera.farClipPlane;
                _captureCamera.depth = sourceCamera.depth - 1;
                _captureCamera.allowHDR = false;
                _captureCamera.allowMSAA = false;

                _renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = "LiveKit_POV_Texture"
                };
                _renderTexture.Create();

                _captureCamera.targetTexture = _renderTexture;
                _captureCamera.enabled = true;

                var additionalData = _captureObject.GetComponent<UniversalAdditionalCameraData>() ??
                                      _captureObject.AddComponent<UniversalAdditionalCameraData>();
                if (additionalData != null)
                {
                    additionalData.renderShadows = false;
                    additionalData.renderPostProcessing = false;
                }

                _videoSource = new TextureVideoSource(_renderTexture, VideoBufferType.Rgba);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public async Task PublishAsync(RoomConnectionHandle handle)
        {
            _publishedHandle = handle ?? throw new ArgumentNullException(nameof(handle));
            _localVideoTrack = LocalVideoTrack.CreateVideoTrack("pov_camera", _videoSource, handle.SdkRoom);
            var options = new TrackPublishOptions
            {
                Source = TrackSource.SourceCamera,
                VideoEncoding = new VideoEncoding
                {
                    MaxBitrate = 1500000,
                    MaxFramerate = (uint)_frameRate
                }
            };

            await handle.Adapter.PublishVideoTrackAsync(_localVideoTrack, options);
        }

        public void BeginFrames()
        {
            _videoSource.Start();
            _videoSourceCoroutine = _coroutineHost.StartLiveKitCoroutine(_videoSource.Update());
        }

        public void Unpublish(RoomConnectionHandle handle)
        {
            if (_unpublished || _localVideoTrack == null || handle == null)
                return;

            _unpublished = true;
            if (_videoSourceCoroutine != null)
            {
                try
                {
                    _coroutineHost.StopLiveKitCoroutine(_videoSourceCoroutine);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] POV coroutine cleanup notice: {exception.Message}");
                }
                _videoSourceCoroutine = null;
            }
            try
            {
                handle.Adapter.UnpublishVideoTrack(_localVideoTrack);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[LiveKitService] POV unpublish cleanup notice: {exception.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (_videoSourceCoroutine != null)
            {
                try
                {
                    _coroutineHost.StopLiveKitCoroutine(_videoSourceCoroutine);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] POV coroutine cleanup notice: {exception.Message}");
                }
                _videoSourceCoroutine = null;
            }

            if (!_unpublished && _localVideoTrack != null && _publishedHandle != null)
                Unpublish(_publishedHandle);

            if (_videoSource != null)
            {
                try
                {
                    _videoSource.Stop();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] VideoSource stop cleanup notice: {exception.Message}");
                }

                try
                {
                    AsyncGPUReadback.WaitAllRequests();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] GPU readback cleanup notice: {exception.Message}");
                }

                try
                {
                    _videoSource.Dispose();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] VideoSource dispose cleanup notice: {exception.Message}");
                }
                _videoSource = null;
            }

            if (_captureCamera != null)
            {
                try
                {
                    _captureCamera.targetTexture = null;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] POV camera detach cleanup notice: {exception.Message}");
                }

                try
                {
                    UnityEngine.Object.Destroy(_captureCamera.gameObject);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] POV camera destroy cleanup notice: {exception.Message}");
                }
                _captureCamera = null;
                _captureObject = null;
            }
            else if (_captureObject != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(_captureObject);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] POV camera destroy cleanup notice: {exception.Message}");
                }
                _captureObject = null;
            }

            if (_renderTexture != null)
            {
                try
                {
                    _renderTexture.Release();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] POV texture release cleanup notice: {exception.Message}");
                }

                try
                {
                    UnityEngine.Object.Destroy(_renderTexture);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[LiveKitService] POV texture destroy cleanup notice: {exception.Message}");
                }
                _renderTexture = null;
            }

            _localVideoTrack = null;
            _publishedHandle = null;
        }
    }
}
