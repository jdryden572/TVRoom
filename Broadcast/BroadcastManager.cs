using LivingRoom.Tuner;
using Microsoft.AspNetCore.SignalR;

namespace LivingRoom.Broadcast
{

    public sealed class BroadcastManager : IDisposable
    {
        private readonly BroadcastSessionFactory _sessionFactory;
        private readonly IHubContext<ControlPanelHub, IControlPanelClient> _controlPanelHub;
        private readonly ILogger _logger;
        private BroadcastSession? _currentSession;

        public BroadcastManager(
            BroadcastSessionFactory sessionFactory,
            IHubContext<ControlPanelHub, IControlPanelClient> controlPanelHub,
            ILogger<BroadcastManager> logger)
        {
            _sessionFactory = sessionFactory;
            _controlPanelHub = controlPanelHub;
            _logger = logger;
        }

        public BroadcastSession? CurrentSession => _currentSession;

        public BroadcastInfo? NowPlaying => 
            _currentSession?.IsReady == true ? _currentSession.BroadcastInfo : null;

        public BroadcastInfo StartSession(ChannelInfo channelInfo, TranscodeOptions transcodeOptions)
        {
            if  (_currentSession is not null)
            {
                throw new InvalidOperationException("Cannot start broadcast when another is already active!");
            }

            var session = _sessionFactory.CreateBroadcast(channelInfo, transcodeOptions);
            _currentSession = session;

            // Start session on background task and notify clients when it is ready
            _ = Task.Run(async () =>
            {
                try
                {
                    await session.StartAndWaitForReadyAsync();
                    await _controlPanelHub.Clients.All.BroadcastStarted(session.BroadcastInfo);
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

                await _controlPanelHub.Clients.All.BroadcastStopped();
            }
        }

        public void Dispose()
        {
            _currentSession?.Dispose();
        }
    }
}
