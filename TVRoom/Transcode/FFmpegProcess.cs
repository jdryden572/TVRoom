using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace TVRoom.Transcode
{
    public sealed partial class FFmpegProcess : IDisposable
    {
        private readonly ILogger _logger;
        private readonly Process _process = new();
        private bool _disposed;
        private bool _stopRequested;
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
            LogStartingFFmpeg(_process.StartInfo.FileName, _process.StartInfo.Arguments);
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

            LogStoppingFFmpeg();

            // Send a "q" keypress and give ffmpeg a chance to stop gracefully.
            // If it doesn't, terminate it with extreme prejudice.
            try
            {
                // might throw if process already stopped (e.g. invalid args)
                _process.StandardInput.Write('q');
            }
            catch (Exception ex)
            {
                LogErrorSendingStopCommand(ex);
            }

            var timeout = TimeSpan.FromSeconds(5);
            LogWaitingForGracefulStop(timeout.TotalSeconds);

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await _process.WaitForExitAsync(cts.Token);
                LogFFmpegStoppedGracefully();
            }
            catch (TaskCanceledException)
            {
                LogFFmpegFailedToStopGracefully(timeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                LogErrorWaitingForFFmpegExit(ex);
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

                LogFFmpegStoppedUnexpectedly();
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
                LogErrorCheckingIfFFmpegRunning(ex);
            }

            if (!hasExited)
            {
                LogKillingFFmpegProcess();
                
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    LogErrorKillingFFmpegProcess(ex);
                }
            }

            _process.Exited -= OnExited;
            _process.Dispose();
            _disposed = true;
            _stopping.OnNext(Unit.Default);
            _stopping.OnCompleted();
            _stopping.Dispose();

            _additionalMessages.OnCompleted();
            _additionalMessages.Dispose();
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Starting ffmpeg process: {FFmpegPath} {Arguments}")]
        private partial void LogStartingFFmpeg(string ffmpegPath, string arguments);

        [LoggerMessage(Level = LogLevel.Information, Message = "Attempting to stop ffmpeg gracefully by writing 'q' to stdin...")]
        private partial void LogStoppingFFmpeg();

        [LoggerMessage(Level = LogLevel.Warning, Message = "Error sending 'q' command to ffmpeg process")]
        private partial void LogErrorSendingStopCommand(Exception ex);

        [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for {TimeoutSeconds}s for ffmpeg to stop gracefully.")]
        private partial void LogWaitingForGracefulStop(double timeoutSeconds);

        [LoggerMessage(Level = LogLevel.Information, Message = "ffmpeg process has stopped gracefully.")]
        private partial void LogFFmpegStoppedGracefully();

        [LoggerMessage(Level = LogLevel.Warning, Message = "ffmpeg did not stop gracefully in {TimeoutSeconds}s")]
        private partial void LogFFmpegFailedToStopGracefully(double timeoutSeconds);

        [LoggerMessage(Level = LogLevel.Error, Message = "Error waiting for ffmpeg process to exit")]
        private partial void LogErrorWaitingForFFmpegExit(Exception ex);

        [LoggerMessage(Level = LogLevel.Error, Message = "ffmpeg process stopped unexpectedly!")]
        private partial void LogFFmpegStoppedUnexpectedly();

        [LoggerMessage(Level = LogLevel.Warning, Message = "Exception received when attempting to check if ffmpeg process was still running")]
        private partial void LogErrorCheckingIfFFmpegRunning(Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Disposing of FFmpegProcess and ffmpeg is still running. Attempting to kill process now.")]
        private partial void LogKillingFFmpegProcess();

        [LoggerMessage(Level = LogLevel.Error, Message = "Error killing ffmpeg process")]
        private partial void LogErrorKillingFFmpegProcess(Exception ex);
    }
}
