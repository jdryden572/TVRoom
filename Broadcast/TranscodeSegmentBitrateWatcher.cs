using System.Diagnostics;

namespace TVRoom.Broadcast
{
    public sealed class TranscodeSegmentBitrateWatcher : BaseObservable<int>, IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public TranscodeSegmentBitrateWatcher(DirectoryInfo transcodeDirectory)
        {
            _watcher = new FileSystemWatcher(transcodeDirectory.FullName, "*.ts");
            _watcher.NotifyFilter = NotifyFilters.Size;
            _watcher.Changed += OnChanged;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType == WatcherChangeTypes.Changed)
            {
                TimeSpan elapsed;
                lock (_stopwatch)
                {
                    elapsed = _stopwatch.Elapsed;
                    _stopwatch.Restart();
                }

                const int bitsPerByte = 8;
                var sizeInBytes = (double)new FileInfo(e.FullPath).Length;
                var rate = sizeInBytes * bitsPerByte / elapsed.TotalSeconds;
                Next((int)rate);
            }
        }

        private void OnError(object sender, ErrorEventArgs e) => Error(e.GetException());

        public void Dispose()
        {
            _watcher.Dispose();
            Complete();
        }
    }
}
