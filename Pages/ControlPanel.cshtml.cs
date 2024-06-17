using LivingRoom.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LivingRoom.Pages
{
    [Authorize(Policies.RequireAdministrator)]
    public class ControlPanelModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}