using Carreiras.Infra.Dados;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Web.Areas.Admin.Pages;

public class EntrarModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _um;
    private readonly AppDbContext _db;

    public EntrarModel(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> um, AppDbContext db)
    {
        _signIn = signIn; _um = um; _db = db;
    }

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Senha { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    public string? Mensagem { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToPage("/Index", new { area = "Admin" });
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        const string generica = "E-mail ou senha incorretos. Confira os dados e tente de novo.";
        var email = (Email ?? "").Trim().ToLowerInvariant();

        var usuario = await _db.UsuarioRhs.SingleOrDefaultAsync(u => u.Email == email);
        // Desativado não entra, e a resposta é a mesma de uma senha errada.
        if (usuario is not { Ativo: true } || usuario.IdentityUserId == null)
        {
            Mensagem = generica;
            return Page();
        }
        var identity = await _um.FindByIdAsync(usuario.IdentityUserId);
        if (identity == null || identity.PasswordHash == null)
        {
            Mensagem = generica;   // convite ainda não aceito
            return Page();
        }

        var r = await _signIn.PasswordSignInAsync(identity, Senha ?? "", isPersistent: false, lockoutOnFailure: true);
        if (r.IsLockedOut)
        {
            Mensagem = "Muitas tentativas. Aguarde 15 minutos e tente de novo.";
            return Page();
        }
        if (!r.Succeeded)
        {
            Mensagem = generica;
            return Page();
        }

        usuario.UltimoAcessoEm = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/admin");
    }
}
