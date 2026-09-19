using System;
using System.Collections.Generic;
using System.Threading;

namespace VRAutism.Cloud.LiveKit
{
    internal sealed class LiveKitMainThreadExecutor
    {
        private readonly object _gate = new object();
        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly int _ownerThreadId;
        private long _generation;
        private bool _closed;

        public LiveKitMainThreadExecutor()
        {
            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        internal bool IsOwnerThread => Thread.CurrentThread.ManagedThreadId == _ownerThreadId;

        internal bool Post(long generation, Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            lock (_gate)
            {
                if (_closed) return false;
                _queue.Enqueue(new WorkItem(generation, action));
                return true;
            }
        }

        internal void AdvanceGeneration(long generation)
        {
            lock (_gate)
            {
                _generation = generation;
            }
        }

        internal void Drain()
        {
            if (!IsOwnerThread)
            {
                throw new InvalidOperationException("LiveKitMainThreadExecutor.Drain must run on its owner thread.");
            }

            while (true)
            {
                WorkItem workItem;
                lock (_gate)
                {
                    if (_queue.Count == 0) return;
                    workItem = _queue.Dequeue();
                    if (workItem.Generation != _generation) continue;
                }

                workItem.Action();
            }
        }

        internal void Close()
        {
            lock (_gate)
            {
                _closed = true;
                _queue.Clear();
            }
        }

        private readonly struct WorkItem
        {
            public long Generation { get; }
            public Action Action { get; }

            public WorkItem(long generation, Action action)
            {
                Generation = generation;
                Action = action;
            }
        }
    }
}
