using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Vagas/Index", new { area = "Admin" });
}
