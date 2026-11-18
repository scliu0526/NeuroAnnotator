using System;
using System.Threading;

namespace Neurocle.WinForms
{
    // 以 Windows 登入工作階段為範圍，保留一個應用程式執行個體。
    internal sealed class SingleInstanceGuard : IDisposable
    {
        internal const string MutexName = @"Local\Neurocle.SingleInstance";
        private readonly Mutex mutex;
        private bool disposed;

        private SingleInstanceGuard(Mutex mutex)
        {
            this.mutex = mutex;
        }

        internal static SingleInstanceGuard TryAcquire()
        {
            var mutex = new Mutex(false, MutexName);
            bool acquired;
            try
            {
                acquired = mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
            {
                mutex.Dispose();
                return null;
            }
            return new SingleInstanceGuard(mutex);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
