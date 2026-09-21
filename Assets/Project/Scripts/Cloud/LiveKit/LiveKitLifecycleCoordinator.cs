using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VRAutism.Cloud.LiveKit
{
    internal enum LiveKitLifecycleState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting,
        Destroyed
    }

    internal sealed class LiveKitLifecycleCoordinator
    {
        private readonly LiveKitRoomConnection _roomConnection;
        private readonly LiveKitMainThreadExecutor _executor;
        private readonly Action _disablePov;
        private readonly Action _stopMicrophone;
        private readonly Action _resetAudio;
        private readonly SemaphoreSlim _lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private readonly object _stateGate = new object();
        private readonly HashSet<RoomConnectionHandle> _pendingTeardownHandles =
            new HashSet<RoomConnectionHandle>();
        private readonly Dictionary<long, CancellationTokenSource> _generationCancellations =
            new Dictionary<long, CancellationTokenSource>();

        private CancellationTokenSource _generationCancellation;
        private long _generation;
        private int _activeOperations;
        private bool _resourcesDisposed;
        private LiveKitLifecycleState _state = LiveKitLifecycleState.Disconnected;

        internal LiveKitLifecycleCoordinator(
            LiveKitRoomConnection roomConnection,
            LiveKitMainThreadExecutor executor,
            Action disablePov,
            Action stopMicrophone,
            Action resetAudio)
        {
            _roomConnection = roomConnection ?? throw new ArgumentNullException(nameof(roomConnection));
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _disablePov = disablePov ?? throw new ArgumentNullException(nameof(disablePov));
            _stopMicrophone = stopMicrophone ?? throw new ArgumentNullException(nameof(stopMicrophone));
            _resetAudio = resetAudio ?? throw new ArgumentNullException(nameof(resetAudio));
        }

        internal LiveKitLifecycleCoordinator(
            ILiveKitRoomAdapterFactory adapterFactory,
            LiveKitMainThreadExecutor executor,
            Action disablePov,
            Action stopMicrophone,
            Action resetAudio)
            : this(new LiveKitRoomConnection(adapterFactory), executor, disablePov, stopMicrophone, resetAudio)
        {
        }

        internal event Action ConnectedOrReconnected;

        internal RoomConnectionHandle CurrentHandle => _roomConnection.CurrentHandle;

        internal bool IsConnected
        {
            get
            {
                lock (_stateGate)
                {
                    return _state == LiveKitLifecycleState.Connected &&
                           _roomConnection.CurrentHandle != null &&
                           _roomConnection.CurrentHandle.IsConnected;
                }
            }
        }

        internal long CurrentGeneration => Interlocked.Read(ref _generation);

        internal LiveKitLifecycleState State
        {
            get
            {
                lock (_stateGate)
                    return _state;
            }
        }

        internal Task ConnectAsync(string roomUrl, string token)
        {
            long generation;
            CancellationTokenSource generationCancellation;
            lock (_stateGate)
            {
                if (_state == LiveKitLifecycleState.Destroyed)
                    return Task.CompletedTask;

                generation = ++_generation;
                _generationCancellation?.Cancel();
                generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetimeCancellation.Token);
                _generationCancellation = generationCancellation;
                _generationCancellations[generation] = generationCancellation;
                _activeOperations++;
                _state = LiveKitLifecycleState.Connecting;
            }

            try
            {
                _executor.AdvanceGeneration(generation);
            }
            catch
            {
                CompleteOperation(generation);
                throw;
            }
            var generationToken = generationCancellation.Token;
            return _executor.PostAsync(
                generation,
                generationToken,
                () => ConnectCoreAsync(generation, generationToken, roomUrl, token));
        }

        internal Task DisconnectAsync()
        {
            long generation;
            CancellationTokenSource generationCancellation;
            lock (_stateGate)
            {
                if (_state == LiveKitLifecycleState.Destroyed)
                    return Task.CompletedTask;

                generation = ++_generation;
                _generationCancellation?.Cancel();
                generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    _lifetimeCancellation.Token);
                _generationCancellation = generationCancellation;
                _generationCancellations[generation] = generationCancellation;
                _activeOperations++;
                _state = LiveKitLifecycleState.Disconnecting;
            }

            try
            {
                _executor.AdvanceGeneration(generation);
            }
            catch
            {
                CompleteOperation(generation);
                throw;
            }
            var generationToken = generationCancellation.Token;
            return _executor.PostAsync(
                generation,
                generationToken,
                () => DisconnectCoreAsync(generation, generationToken));
        }

        internal void Destroy()
        {
            RoomConnectionHandle[] handles;
            lock (_stateGate)
            {
                if (_state == LiveKitLifecycleState.Destroyed)
                    return;

                ++_generation;
                _generationCancellation?.Cancel();
                _lifetimeCancellation.Cancel();
                _state = LiveKitLifecycleState.Destroyed;
                QueueTeardownNoLock(_roomConnection.CurrentHandle);
                handles = SnapshotPendingTeardownNoLock();
            }

            foreach (var pendingHandle in _roomConnection.GetAllHandles())
            {
                lock (_stateGate)
                    QueueTeardownNoLock(pendingHandle);
            }

            lock (_stateGate)
                handles = SnapshotPendingTeardownNoLock();

            _executor.Close();
            foreach (var handle in handles)
                Teardown(handle);

            // Drain already-started continuations synchronously when Destroy is called
            // on the owner thread. Later SDK completions use the executor's captured
            // owner SynchronizationContext after Close.
            if (_executor.IsOwnerThread)
                _executor.Drain();
            MaybeDisposeResources();
        }

        private async Task ConnectCoreAsync(
            long generation,
            CancellationToken generationToken,
            string roomUrl,
            string token)
        {
            var entered = false;
            try
            {
                if (!_lifecycleGate.Wait(0))
                {
                    await _lifecycleGate.WaitAsync(generationToken);
                    entered = true;
                }
                else
                {
                    entered = true;
                }
                if (!IsGenerationCurrent(generation, generationToken))
                    return;

                TeardownPending();

                RoomConnectionHandle candidate;
                try
                {
                    candidate = await _roomConnection.CreateConnectedAsync(
                        generation,
                        roomUrl,
                        token,
                        generationToken);
                }
                catch (OperationCanceledException) when (!IsGenerationCurrent(generation, generationToken))
                {
                    return;
                }

                if (!IsGenerationCurrent(generation, generationToken))
                {
                    _roomConnection.DetachAndDisconnect(candidate);
                    return;
                }

                _roomConnection.Commit(candidate);
                lock (_stateGate)
                {
                    if (!IsGenerationCurrentNoLock(generation, generationToken))
                    {
                        _roomConnection.DetachAndDisconnect(candidate);
                        return;
                    }

                    _state = LiveKitLifecycleState.Connected;
                }

                _executor.Post(generation, () =>
                {
                    lock (_stateGate)
                    {
                        if (_state != LiveKitLifecycleState.Connected ||
                            generation != _generation ||
                            !ReferenceEquals(_roomConnection.CurrentHandle, candidate))
                            return;
                    }

                    ConnectedOrReconnected?.Invoke();
                });
            }
            catch (OperationCanceledException) when (!IsGenerationCurrent(generation, generationToken))
            {
            }
            catch (Exception exception)
            {
                lock (_stateGate)
                {
                    if (IsGenerationCurrentNoLock(generation, generationToken))
                        _state = LiveKitLifecycleState.Disconnected;
                }

                throw new InvalidOperationException($"LiveKit room connection failed: {exception.Message}", exception);
            }
            finally
            {
                if (entered)
                {
                    _lifecycleGate.Release();
                }

                CompleteOperation(generation);
            }
        }

        private async Task DisconnectCoreAsync(
            long generation,
            CancellationToken generationToken)
        {
            var entered = false;
            try
            {
                if (!_lifecycleGate.Wait(0))
                {
                    await _lifecycleGate.WaitAsync(generationToken);
                    entered = true;
                }
                else
                {
                    entered = true;
                }
                lock (_stateGate)
                {
                    if (_state == LiveKitLifecycleState.Destroyed || generation != _generation)
                        return;
                }

                TeardownPending();
                lock (_stateGate)
                {
                    if (_state != LiveKitLifecycleState.Destroyed && generation == _generation)
                        _state = LiveKitLifecycleState.Disconnected;
                }
            }
            catch (OperationCanceledException) when (!IsGenerationCurrent(generation, generationToken))
            {
            }
            finally
            {
                if (entered)
                {
                    _lifecycleGate.Release();
                }

                CompleteOperation(generation);
            }
        }

        private bool IsGenerationCurrent(long generation, CancellationToken token)
        {
            lock (_stateGate)
                return IsGenerationCurrentNoLock(generation, token);
        }

        private bool IsGenerationCurrentNoLock(long generation, CancellationToken token)
        {
            return !token.IsCancellationRequested &&
                   _state != LiveKitLifecycleState.Destroyed &&
                   generation == _generation;
        }

        private void TeardownPending()
        {
            RoomConnectionHandle[] handles;
            lock (_stateGate)
            {
                QueueTeardownNoLock(_roomConnection.CurrentHandle);
                foreach (var pendingHandle in _roomConnection.GetPendingCleanupHandles())
                    QueueTeardownNoLock(pendingHandle);
                handles = SnapshotPendingTeardownNoLock();
            }

            foreach (var handle in handles)
                Teardown(handle);
        }

        private void Teardown(RoomConnectionHandle handle)
        {
            if (handle == null)
                return;

            lock (_stateGate)
                QueueTeardownNoLock(handle);

            handle.RunPovTeardown(_disablePov);
            handle.RunMicrophoneTeardown(_stopMicrophone);
            handle.RunAudioTeardown(_resetAudio);
            try
            {
                _roomConnection.DetachAndDisconnect(handle);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"[LiveKitService] Room teardown notice: {exception.Message}");
            }

            if (handle.DeviceTeardownComplete && _roomConnection.IsCleanupComplete(handle))
            {
                lock (_stateGate)
                    _pendingTeardownHandles.Remove(handle);
            }
        }

        private void QueueTeardownNoLock(RoomConnectionHandle handle)
        {
            if (handle != null)
                _pendingTeardownHandles.Add(handle);
        }

        private RoomConnectionHandle[] SnapshotPendingTeardownNoLock() =>
            new List<RoomConnectionHandle>(_pendingTeardownHandles).ToArray();

        private void CompleteOperation(long generation)
        {
            CancellationTokenSource generationCancellation = null;
            lock (_stateGate)
            {
                if (_generationCancellations.TryGetValue(generation, out generationCancellation))
                    _generationCancellations.Remove(generation);
                if (ReferenceEquals(_generationCancellation, generationCancellation))
                    _generationCancellation = null;
                _activeOperations--;
            }

            generationCancellation?.Dispose();
            MaybeDisposeResources();
        }

        private void MaybeDisposeResources()
        {
            CancellationTokenSource lifetimeCancellation = null;
            SemaphoreSlim lifecycleGate = null;
            lock (_stateGate)
            {
                if (_resourcesDisposed || _state != LiveKitLifecycleState.Destroyed || _activeOperations != 0)
                    return;

                _resourcesDisposed = true;
                lifetimeCancellation = _lifetimeCancellation;
                lifecycleGate = _lifecycleGate;
            }

            lifetimeCancellation.Dispose();
            lifecycleGate.Dispose();
        }
    }
}
