using TVRoom.Tuner;
using Microsoft.AspNetCore.SignalR;
using static System.Formats.Asn1.AsnWriter;

namespace TVRoom.Broadcast
{

    public sealed class BroadcastManager : IDisposable
    {
        private readonly BroadcastSessionFactory _sessionFactory;
        private readonly BroadcastHistoryService _historyService;
        private readonly IHubContext<ControlPanelHub, IControlPanelClient> _controlPanelHub;
        private readonly ILogger _logger;
        private BroadcastSession? _currentSession;

        public BroadcastManager(
            IHubContext<ControlPanelHub, IControlPanelClient> controlPanelHub,
            ILogger<BroadcastManager> logger,
            BroadcastSessionFactory sessionFactory,
            BroadcastHistoryService historyService)
        {
            _controlPanelHub = controlPanelHub;
            _logger = logger;
            _sessionFactory = sessionFactory;
            _historyService = historyService;
        }

        public BroadcastSession? CurrentSession => _currentSession;

        public BroadcastInfo? NowPlaying => 
            _currentSession?.IsReady == true ? _currentSession.BroadcastInfo : null;

        public async Task<BroadcastInfo> StartSession(ChannelInfo channelInfo)
        {
            if  (_currentSession is not null)
            {
                throw new InvalidOperationException("Cannot start broadcast when another is already active!");
            }

            var session = _sessionFactory.CreateBroadcast(channelInfo);

            await _historyService.StartNewBroadcast(channelInfo);

            _currentSession = session;
            await _controlPanelHub.Clients.All.BroadcastStarted(session.BroadcastInfo);

            // Start session on background task and notify clients when it is ready
            _ = Task.Run(async () =>
            {
                try
                {
                    await session.StartAndWaitForReadyAsync();
                    await _controlPanelHub.Clients.All.BroadcastReady(session.BroadcastInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while waiting for broadcast to become ready");
                }
            });

            return session.BroadcastInfo;
        }

        public async Task<ChannelInfo?> GetLastChannel()
        {
            var latest = await _historyService.GetLatestBroadcast();
            if (latest is null)
            {
                return null;
            }

            return new ChannelInfo(latest.GuideNumber, latest.GuideName, latest.Url);
        }

        public async Task StopSessionAsync()
        {
            if (_currentSession is not null)
            {
                await _currentSession.HlsLiveStream.StopAsync();
                _currentSession.Dispose();
                _currentSession = null;

                await _historyService.EndCurrentBroadcast();
                await _controlPanelHub.Clients.All.BroadcastStopped();
            }
        }

        public async Task RestartTranscode()
        {
            if (_currentSession is not null)
            {
                await _currentSession.HlsLiveStream.RestartAsync();
            }
        }

        public void Dispose()
        {
            _currentSession?.Dispose();
        }
    }
}
