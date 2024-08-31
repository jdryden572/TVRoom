using System.Diagnostics;

namespace TVRoom.Broadcast
{
    public class FFmpegProcess : BaseObservable<string>, IDisposable
    {
        private readonly ILogger _logger;
        private readonly Process _process = new();
        private bool _disposed = false;
        private bool _stopRequested = false;

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
            _process.ErrorDataReceived += (_, e) => Next(e.Data ?? string.Empty);
            _process.Exited += OnExited;
        }

        public void Start()
        {
            _logger.LogInformation("Starting ffmpeg process: {ffmpegPath} {arguments}", _process.StartInfo.FileName, _process.StartInfo.Arguments);
            _process.Start();
            _process.BeginErrorReadLine();
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
            if (!_stopRequested)
            {
                // The process exited before we stopped it, something has gone wrong.
                _logger.LogError("ffmpeg process stopped unexpectedly!");
                Error(new Exception("ffmpeg process stopped unexpectedly!"));
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _process.Exited -= OnExited;

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
                    _process.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error killing ffmpeg process");
                }
            }

            _process.Dispose();
            _disposed = true;
            Complete();
        }
    }    
}
