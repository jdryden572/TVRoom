using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TVRoom.Pages
{
    [AllowAnonymous]
    public class LoginModel : PageModel
    {
        [FromQuery]
        public string ReturnUrl { get; set; } = string.Empty;

        [FromQuery]
        public bool Prompt { get; set; }

        public IActionResult OnGet()
        {
            if (!Url.IsLocalUrl(ReturnUrl))
            {
                return BadRequest();
            }

            var props = new AuthenticationProperties { RedirectUri = ReturnUrl };
            props.SetParameter("Prompt", Prompt);
            return new ChallengeResult(GoogleDefaults.AuthenticationScheme, props);
        }
    }
}
