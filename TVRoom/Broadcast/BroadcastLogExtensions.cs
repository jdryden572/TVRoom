﻿using Serilog;
using System.Globalization;
using System.Reactive.Linq;
using TVRoom.Configuration;

namespace TVRoom.Broadcast
{
    internal static class BroadcastLogExtensions
    {
        public static void WriteTranscodeLogsToFile(this IObservable<string> transcodeOutput, BroadcastInfo broadcastInfo, HlsConfiguration hlsConfig)
        {
            var timeString = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            var logName = $"{timeString}_Channel{broadcastInfo.ChannelInfo.GuideNumber.Replace('.', '-')}_{broadcastInfo.SessionId}.log";
            var logPath = Path.Combine(hlsConfig.LogDirectory.FullName, logName);
            var log = new LoggerConfiguration()
                .WriteTo.File(logPath, flushToDiskInterval: TimeSpan.FromSeconds(30), formatProvider: CultureInfo.InvariantCulture)
                .CreateLogger();

            transcodeOutput
                .Finally(log.Dispose)
                .Subscribe(
                    log.Information,
                    e => log.Error(e, "Error from transcode output"),
                    () => log.Warning("Transcode output completed!"));
        }
    }
}
