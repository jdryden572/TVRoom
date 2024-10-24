using TVRoom.Authorization;
using TVRoom.Tuner;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace TVRoom.Broadcast
{
    public interface IControlPanelClient
    {
        Task BroadcastStarted(BroadcastInfo info);
        Task BroadcastReady(BroadcastInfo info);
        Task BroadcastStopped();
    }

    [Authorize(Policies.RequireAdministrator)]
    public class ControlPanelHub : Hub<IControlPanelClient>
    {
        private readonly BroadcastManager _broadcastManager;
        private readonly TunerClient _tunerClient;
        private readonly TunerStatusProvider _tunerStatusProvider;

        public ControlPanelHub(BroadcastManager broadcastManager, TunerClient tunerClient, TunerStatusProvider tunerStatusProvider)
        {
            _broadcastManager = broadcastManager;
            _tunerClient = tunerClient;
            _tunerStatusProvider = tunerStatusProvider;
        }

        public async Task<BroadcastInfo> StartBroadcast(string guideNumber)
        {
            var channel = await _tunerClient.GetChannelAsync(guideNumber);
            if (channel is null)
            {
                throw new HubException($"Channel with guideNumber='{guideNumber}' not found");
            }

            return await _broadcastManager.StartSession(channel);
        }

        public async Task StopBroadcast()
        {
            await _broadcastManager.StopSessionAsync();
        }

        public async Task<ChannelInfo?> GetLastChannel()
        {
            return await _broadcastManager.GetLastChannel();
        }

        public BroadcastInfo? GetCurrentSession()
        {
            return _broadcastManager.CurrentSession?.BroadcastInfo;
        }

        public ChannelReader<string> GetDebugOutput(CancellationToken cancellation)
        {
            var currentSession = _broadcastManager.CurrentSession;
            if (currentSession is null)
            {
                throw new InvalidOperationException("No broadcast session is active");
            }

            return currentSession.GetDebugOutput(cancellation);
        }

        public ChannelReader<TunerStatus[]> GetTunerStatuses(CancellationToken cancellation) => _tunerStatusProvider.Statuses.AsChannelReader(cancellation);
    }
}
