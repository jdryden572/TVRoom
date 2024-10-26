using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace TVRoom.Transcode
{
    public class FFmpegProcess : IDisposable
    {
        private readonly ILogger _logger;
        private readonly Process _process = new();
        private bool _disposed = false;
        private bool _stopRequested = false;
        private Subject<Unit> _stopping = new();
        private Subject<string> _additionalMessages = new();

        public FFmpegProcess(string ffmpegPath, string arguments, ILogger logger)
        {
            _logger = logger;

            _process.StartInfo.FileName = ffmpegPath;
            _process.StartInfo.Arguments = arguments;

            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.EnableRaisingEvents = true;
            _process.Exited += OnExited;

            _ffmpegOutput = Observable.FromEventPattern<DataReceivedEventHandler, DataReceivedEventArgs>(
                    h => _process.ErrorDataReceived += h,
                    h => _process.ErrorDataReceived -= h)
                .Select(e => e.EventArgs.Data ?? string.Empty)
                .Merge(_additionalMessages)
                .TakeUntil(_stopping)
                .Publish();
        }

        public string Arguments => _process.StartInfo.Arguments;

        private readonly IConnectableObservable<string> _ffmpegOutput;
        public IObservable<string> FFmpegOutput => _ffmpegOutput;

        public void Start()
        {
            _logger.LogInformation("Starting ffmpeg process: {ffmpegPath} {arguments}", _process.StartInfo.FileName, _process.StartInfo.Arguments);
            _process.Start();
            _process.BeginErrorReadLine();
            _ffmpegOutput.Connect();
        }

        public async Task StopAsync()
        {
            if (_stopRequested || _disposed)
            {
                return;
            }

            _stopRequested = true;

            _logger.LogInformation("Attempting to stop ffmpeg gracefully by writing 'q' to stdin...");

            // Send a "q" keypress and give ffmpeg a chance to stop gracefully.
            // If it doesn't, terminate it with extreme prejudice.
            try
            {
                // might throw if process already stopped (e.g. invalid args)
                _process.StandardInput.Write('q');
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending 'q' command to ffmpeg process");
            }

            var timeout = TimeSpan.FromSeconds(5);
            _logger.LogInformation("Waiting for {timeoutSeconds}s for ffmpeg to stop gracefully.", timeout.TotalSeconds);

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await _process.WaitForExitAsync(cts.Token);
                _logger.LogInformation("ffmpeg process has stopped gracefully.");
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("ffmpeg did not stop gracefully in {timeoutSeconds}s", timeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for process to exit");
            }

            Dispose();
        }

        private void OnExited(object? sender, EventArgs e)
        {
            // Ensure that StdErr events are flushed to our observable
            _process.WaitForExit();

            if (!_stopRequested)
            {
                // The process exited before we stopped it, something has gone wrong.
                _logger.LogError("ffmpeg process stopped unexpectedly!");
                _additionalMessages.OnNext("ffmpeg process stopped unexpectedly!");
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            bool hasExited = false;
            try
            {
                hasExited = _process.HasExited;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception received when attempting to check if ffmpeg process was still running");
            }

            if (!hasExited)
            {
                _logger.LogInformation("Disposing of FFmpegProcess and ffmpeg is still running. Attempting to kill process now.");
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error killing ffmpeg process");
                }
            }

            _process.Exited -= OnExited;
            _process.Dispose();
            _disposed = true;
            _stopping.OnNext(Unit.Default);
        }
    }
}
