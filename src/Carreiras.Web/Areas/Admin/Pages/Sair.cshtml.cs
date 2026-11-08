using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages;

public class SairModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signIn;
    public SairModel(SignInManager<IdentityUser> signIn) => _signIn = signIn;

    public IActionResult OnGet() => RedirectToPage("/Index", new { area = "Admin" });

    public async Task<IActionResult> OnPostAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Entrar", new { area = "Admin" });
    }
}
