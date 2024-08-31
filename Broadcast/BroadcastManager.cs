using TVRoom.Tuner;
using Microsoft.AspNetCore.SignalR;

namespace TVRoom.Broadcast
{

    public sealed class BroadcastManager : IDisposable
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IHubContext<ControlPanelHub, IControlPanelClient> _controlPanelHub;
        private readonly ILogger _logger;
        private BroadcastSession? _currentSession;

        public BroadcastManager(
            IServiceScopeFactory serviceScopeFactory,
            IHubContext<ControlPanelHub, IControlPanelClient> controlPanelHub,
            ILogger<BroadcastManager> logger)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _controlPanelHub = controlPanelHub;
            _logger = logger;
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

            BroadcastSession session;
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var sessionFactory = scope.ServiceProvider.GetRequiredService<BroadcastSessionFactory>();
                session = await sessionFactory.CreateBroadcast(channelInfo);

                var historyService = scope.ServiceProvider.GetRequiredService<BroadcastHistoryService>();
                await historyService.StartNewBroadcast(channelInfo);
            }

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
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var historyService = scope.ServiceProvider.GetRequiredService<BroadcastHistoryService>();
                var latest = await historyService.GetLatestBroadcast();
                if (latest is null)
                {
                    return null;
                }

                return new ChannelInfo(latest.GuideNumber, latest.GuideName, latest.Url);
            }
        }

        public async Task StopSessionAsync()
        {
            if (_currentSession is not null)
            {
                await _currentSession.StopAsync();
                _currentSession.Dispose();
                _currentSession = null;

                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    var historyService = scope.ServiceProvider.GetRequiredService<BroadcastHistoryService>();
                    await historyService.EndCurrentBroadcast();
                }

                await _controlPanelHub.Clients.All.BroadcastStopped();
            }
        }

        public void Dispose()
        {
            _currentSession?.Dispose();
        }
    }
}
