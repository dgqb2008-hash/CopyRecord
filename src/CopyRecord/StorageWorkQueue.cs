using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CopyRecord
{
    internal sealed class StorageWorkQueue : IDisposable
    {
        private readonly BlockingCollection<Action> _jobs = new BlockingCollection<Action>();
        private readonly Thread _worker;
        private bool _disposed;

        internal event Action<Exception> Failed;

        internal StorageWorkQueue()
        {
            _worker = new Thread(WorkLoop)
            {
                IsBackground = true,
                Name = "CopyRecord.Storage"
            };
            _worker.Start();
        }

        internal void Enqueue(Action job)
        {
            if (job == null || _disposed) return;
            try { _jobs.Add(job); }
            catch (InvalidOperationException) { }
        }

        private void WorkLoop()
        {
            foreach (Action job in _jobs.GetConsumingEnumerable())
            {
                try { job(); }
                catch (Exception exception)
                {
                    Action<Exception> handler = Failed;
                    if (handler != null) handler(exception);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _jobs.CompleteAdding();
            _worker.Join();
            _jobs.Dispose();
        }
    }
}
