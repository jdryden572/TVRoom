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

            _currentSession = session;
            await _controlPanelHub.Clients.All.BroadcastStarted(session.BroadcastInfo);

            _ = NotifyWhenReady(session);
            _ = NotifyWhenStopped(session);

            return session.BroadcastInfo;
        }

        private async Task NotifyWhenReady(BroadcastSession session)
        {
            await session.HlsLiveStream.Ready;
            await _controlPanelHub.Clients.All.BroadcastReady(session.BroadcastInfo);
        }

        private async Task NotifyWhenStopped(BroadcastSession session)
        {
            await session.Finished;

            session.Dispose();
            _currentSession = null;

            await _controlPanelHub.Clients.All.BroadcastStopped();
        }

        public async Task StopSessionAsync()
        {
            if (_currentSession is not null)
            {
                await _currentSession.StopAsync();
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
