using EliteJournalReader.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace EliteJournalReader
{

    /// <summary>
    /// File watcher and parser for the status.json to be introduced in Elite:Dangerous 3.0.
    /// This file is regenerated approximately every second.
    /// The file contains a single json statement that is fired off as a .NET event
    /// </summary>
    public class StatusWatcher : FileSystemWatcher
    {
        public event EventHandler<StatusFileEvent> StatusUpdated;

        /// <summary>
        ///     The default filter
        /// </summary>
        private const string DefaultFilter = @"Status.json";

        private readonly object updateLock = new object();
        private Timer pollTimer;
        private JToken lastPayload;

        /// <summary>
        /// Token to signal that we are no longer watching
        /// </summary>
        private CancellationTokenSource cancellationTokenSource;

        public StatusFileEvent LastEvent { get; protected set; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="T:System.Object" /> class.
        /// </summary>
        public StatusWatcher(string path)
        {
            Initialize(path);
        }

        protected void Initialize(string path)
        {
            Filter = DefaultFilter;
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size;

            try
            {
                Path = System.IO.Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Exception in setting path: " + ex.Message);
            }
        }

        /// <summary>
        /// For unit tests only
        /// </summary>
        protected StatusWatcher()
        {
        }

        /// <summary>
        ///     Starts the watching.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        ///     Throws an exception if the <see cref="Path" /> does not contain netLogs
        ///     files.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        ///     The directory specified in <see cref="P:System.IO.FileSystemWatcher.Path" />
        ///     could not be found.
        /// </exception>
        public virtual void StartWatching()
        {
            if (EnableRaisingEvents)
            {
                // Already watching
                return;
            }

            if (!Directory.Exists(Path))
            {
                Trace.TraceError($"Cannot watch non-existing folder {Path}.");
                return;
            }

            if (cancellationTokenSource != null)
                cancellationTokenSource.Cancel(); // should not happen, but let's be safe, okay?

            cancellationTokenSource = new CancellationTokenSource();

            Changed -= UpdateStatus;
            Changed += UpdateStatus;
            Created -= UpdateStatus;
            Created += UpdateStatus;
            Renamed -= UpdateStatus;
            Renamed += UpdateStatus;

            EnableRaisingEvents = true;

            // start with reading any existing status
            string statusJsonPath = System.IO.Path.Combine(Path, "Status.json");
            if (File.Exists(statusJsonPath))
                UpdateStatus(statusJsonPath, 0);

            // Recover missed/overflowed filesystem notifications and atomic file replacement.
            pollTimer = new Timer(_ =>
            {
                if (!cancellationTokenSource.IsCancellationRequested)
                    UpdateStatus(statusJsonPath, 0);
            }, null, 1000, 1000);
        }

        public virtual void StopWatching()
        {
            try
            {
                if (EnableRaisingEvents)
                {
                    Changed -= UpdateStatus;
                    Created -= UpdateStatus;
                    Renamed -= UpdateStatus;
                    pollTimer?.Dispose();
                    pollTimer = null;

                    if (cancellationTokenSource != null)
                        cancellationTokenSource.Cancel();

                    EnableRaisingEvents = false;
                }
            }
            catch (Exception e)
            {
                Trace.TraceError($"Error while stopping Status watcher: {e.Message}");
                Trace.TraceInformation(e.StackTrace);
            }
        }

        protected void UpdateStatus(object sender, FileSystemEventArgs e) => UpdateStatus(e.FullPath, 0);

        protected void UpdateStatus(string fullPath, int attempt)
        {
            lock (updateLock)
            {
                ReadStatus(fullPath, attempt);
            }
        }

        private void ReadStatus(string fullPath, int attempt)
        {
            try
            {
                //Thread.Sleep(50); // give it a wee bit
                using var streamReader = new StreamReader(new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                using (var jsonTextReader = new JsonTextReader(streamReader))
                {
                    if (streamReader.EndOfStream)
                        throw new IOException("Status.json is temporarily empty");

                    var jToken = JToken.ReadFrom(jsonTextReader);
                    var evt = jToken.ToObject<StatusFileEvent>() ?? throw new ArgumentNullException($"Unexpected empty status.json file");
                    evt.OriginalEvent = jToken;

                    // only fire the event if it's new data
                    // Timestamps have only second precision; multiple real changes can share one.
                    // Also accept a new session after the system clock moves backwards.
                    if (!JToken.DeepEquals(jToken, lastPayload))
                    {
                        lastPayload = jToken.DeepClone();
                        LastEvent = evt;
                        FireStatusUpdatedEvent(evt);
                    }
                }
            }
            catch (IOException ioe) { HandleUpdateStatusException(ioe, fullPath, attempt); }
            catch (JsonException je) { HandleUpdateStatusException(je, fullPath, attempt); }
#if DEBUG
            catch (Exception ex)
            {
                Trace.TraceInformation($"Error while reading from status.json: {ex.Message}\n{ex.StackTrace}");
#else
            catch (Exception)
            {
#endif
                // Keep the last valid state when a write races this read.
            }
        }

        private void HandleUpdateStatusException(Exception ex, string fullPath, int attempt)
        {
            // it could be that we are trying to read at the exact same time the 
            // game is writing a new status.json. To handle this case, we simply give it another shot.
            if (attempt < 5)
            {
                Thread.Sleep(25);
                ReadStatus(fullPath, attempt + 1);
            }
            else
            {
                Trace.TraceWarning($"{ex.GetType().Name} while reading from status.json: {ex.Message}");
                Trace.TraceInformation(ex.StackTrace);
                // The next notification or poll retries; never invent an all-off status.
            }
        }


        protected void FireStatusUpdatedEvent(StatusFileEvent evt) => StatusUpdated?.Invoke(this, evt);

        /// <summary>
        /// When an error occurs, set some status flags to signal that the status is not valid
        /// And to prevent logic from sending data to EDSM/Inara.
        /// </summary>
        protected void FireFailedStatusUpdateEvent()
        {
            FireStatusUpdatedEvent(new StatusFileEvent
            {
                Flags = StatusFlags.None,
                Flags2 = MoreStatusFlags.InMulticrew & MoreStatusFlags.PhysicalMulticrew & MoreStatusFlags.TelepresenceMulticrew,
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopWatching();
                pollTimer?.Dispose();
                cancellationTokenSource?.Cancel();
            }
            base.Dispose(disposing);
        }
    }

}
