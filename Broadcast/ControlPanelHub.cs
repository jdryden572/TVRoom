using LivingRoom.Authorization;
using LivingRoom.Tuner;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Threading.Channels;

namespace LivingRoom.Broadcast
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

        public ControlPanelHub(BroadcastManager broadcastManager, TunerClient tunerClient) => 
            (_broadcastManager, _tunerClient) = (broadcastManager, tunerClient);

        public async Task<BroadcastInfo> StartBroadcast(string guideNumber, TranscodeOptions transcodeOptions)
        {
            var channel = await _tunerClient.GetChannelAsync(guideNumber);
            if (channel is null)
            {
                throw new HubException($"Channel with guideNumber='{guideNumber}' not found");
            }

            return await _broadcastManager.StartSession(channel, transcodeOptions);
        }

        public async Task StopBroadcast()
        {
            await _broadcastManager.StopSessionAsync();
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
    }
}
