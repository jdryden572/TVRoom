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
        Task BroadcastStopped();
    }

    [Authorize(Policies.RequireAdministrator)]
    public class ControlPanelHub : Hub<IControlPanelClient>
    {
        private readonly BroadcastManager _broadcastManager;

        public ControlPanelHub(BroadcastManager broadcastManager) => 
            _broadcastManager = broadcastManager;

        public BroadcastInfo StartBroadcast(ChannelInfo channelInfo, TranscodeOptions transcodeOptions)
        {
            return _broadcastManager.StartSession(channelInfo, transcodeOptions);
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
