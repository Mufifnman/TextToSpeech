

using System;

namespace TextToSpeech
{
    public class Singleton<T> : IDisposable where T : Singleton<T>
    {
        private static readonly Lazy<T> lazy = new Lazy<T>(() => (T)Activator.CreateInstance(typeof(T), true));

        public static T Instance
        {
            get
            {
                return lazy.Value;
            }
        }

        public static bool IsInitialized => lazy.IsValueCreated;

        protected Singleton()
        {
            // Constructor is protected. No direct instantiation.
        }

        // Common utility methods and properties can be added here   private bool disposed = false;
        #region Disposal
        private bool disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources.
                }

                // Dispose unmanaged resources.
                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion Disposal
    }
}