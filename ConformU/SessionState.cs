﻿using System;

namespace ConformU
{
    public class SessionState : IDisposable
    {
        private bool disposedValue;
        private readonly ConformLogger TL;

        public SessionState(ConformLogger logger)
        {
            TL = logger;
            TL?.LogMessage("ConformStateManager", MessageLevel.Debug, "Service started");
        }

        public event EventHandler UiHasChanged;

        public void RaiseUiHasChangedEvent()
        {
            if (UiHasChanged is not null)
            {
                EventArgs args = new();
                TL?.LogMessage("RaiseUiHasChangedEvent", MessageLevel.Debug, "About to call UI has changed event handler");
                UiHasChanged(this, args);
                TL?.LogMessage("RaiseUiHasChangedEvent", MessageLevel.Debug, "Returned from UI has changed event handler");
            }
        }

        public string ConformLog { get; set; }

        /// <summary>
        /// Current value of the main ConformLog window scroll position
        /// </summary>
        /// <remarks>Used to restore the scroll position when the user does a browser refresh or returns to the home page.</remarks>
        public double ConformLogScrollTop { get; set; } = 0;

        public bool SafetyWarningDisplayed { get; set; } = false;

        public string ProtocolLog { get; set; }

        #region IDisposable support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~ConformStateManager()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
