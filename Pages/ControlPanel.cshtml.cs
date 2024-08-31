using TVRoom.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TVRoom.Pages
{
    [Authorize(Policies.RequireAdministrator)]
    public class ControlPanelModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
