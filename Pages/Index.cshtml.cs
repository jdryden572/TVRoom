using TVRoom.Broadcast;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RazorPagesWithVite.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly BroadcastManager _broadcastManager;

        public BroadcastInfo? CurrentBroadcast { get; private set; }

        public IndexModel(ILogger<IndexModel> logger, BroadcastManager broadcastManager)
        {
            _logger = logger;
            _broadcastManager = broadcastManager;
        }

        public void OnGet()
        {
            CurrentBroadcast = _broadcastManager.NowPlaying;
        }
    }
}
