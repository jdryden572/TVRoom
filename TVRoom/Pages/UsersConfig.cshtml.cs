using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TVRoom.Authorization;

namespace TVRoom.Pages
{
    [Authorize(Policies.RequireAdministrator)]
    public class UsersConfigModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
