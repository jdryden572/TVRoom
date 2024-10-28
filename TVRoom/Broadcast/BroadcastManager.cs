using Microsoft.AspNetCore.SignalR;
using TVRoom.Configuration;
using TVRoom.Tuner;

namespace TVRoom.Broadcast
{

    public sealed class BroadcastManager : IDisposable
    {
        private readonly BroadcastSessionFactory _sessionFactory;
        private readonly IHubContext<ControlPanelHub, IControlPanelClient> _controlPanelHub;
        private readonly HlsConfiguration _hlsConfiguration;
        private readonly ILogger _logger;
        private BroadcastSession? _currentSession;
        private CancellationTokenSource _autoStopBroadcastCts = new();

        public BroadcastManager(
            IHubContext<ControlPanelHub, IControlPanelClient> controlPanelHub,
            ILogger<BroadcastManager> logger,
            BroadcastSessionFactory sessionFactory,
            HlsConfiguration hlsConfiguration)
        {
            _controlPanelHub = controlPanelHub;
            _logger = logger;
            _sessionFactory = sessionFactory;
            _hlsConfiguration = hlsConfiguration;
        }

        public BroadcastSession? CurrentSession => _currentSession;

        public BroadcastInfo? NowPlaying => 
            _currentSession?.HlsLiveStream.IsReady == true ? _currentSession.BroadcastInfo : null;

        public async Task<BroadcastInfo> StartSession(ChannelInfo channelInfo)
        {
            if  (_currentSession is not null)
            {
                throw new InvalidOperationException("Cannot start broadcast when another is already active!");
            }

            var session = await _sessionFactory.CreateBroadcast(channelInfo);

            await session.StartAsync();

            _autoStopBroadcastCts.CancelAfter(_hlsConfiguration.MaxDuration);
            _autoStopBroadcastCts.Token.Register(() => Task.Run(StopSessionAsync));

            _currentSession = session;
            await _controlPanelHub.Clients.All.BroadcastStarted(session.BroadcastInfo);

            // Start session on background task and notify clients when it is ready
            _ = Task.Run(async () =>
            {
                try
                {
                    await session.HlsLiveStream.Ready;
                    await _controlPanelHub.Clients.All.BroadcastReady(session.BroadcastInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while waiting for broadcast to become ready");
                }
            });

            return session.BroadcastInfo;
        }

        public async Task StopSessionAsync()
        {
            if (_currentSession is not null)
            {
                await _currentSession.StopAsync();
                _currentSession.Dispose();
                _currentSession = null;
                _autoStopBroadcastCts = new();

                await _controlPanelHub.Clients.All.BroadcastStopped();
            }
        }

        public async Task RestartTranscodeAsync()
        {
            if (_currentSession is not null)
            {
                await _currentSession.RestartTranscodeAsync();
            }
        }

        public void Dispose()
        {
            _currentSession?.Dispose();
        }
    }
}
