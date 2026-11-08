using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Carreiras.Web.Areas.Admin.Pages;

/// <summary>Convite e "nova senha": a própria pessoa cria a senha por link de uso único com prazo (RN18).</summary>
public class DefinirSenhaModel : PageModel
{
    private readonly UserManager<IdentityUser> _um;
    public DefinirSenhaModel(UserManager<IdentityUser> um) => _um = um;

    [BindProperty(SupportsGet = true)] public string? Uid { get; set; }
    [BindProperty(SupportsGet = true)] public string? Token { get; set; }
    [BindProperty] public string Senha { get; set; } = "";
    [BindProperty] public string Confirmacao { get; set; } = "";
    public bool LinkInvalido { get; set; }
    public List<string> Erros { get; } = new();

    public async Task OnGetAsync() => LinkInvalido = !await TokenValidoAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = string.IsNullOrEmpty(Uid) ? null : await _um.FindByIdAsync(Uid);
        var bruto = Decodificar(Token);
        if (user == null || bruto == null) { LinkInvalido = true; return Page(); }

        if (Senha != Confirmacao) { Erros.Add("As senhas não são iguais. Digite a mesma senha nos dois campos."); return Page(); }

        var r = await _um.ResetPasswordAsync(user, bruto, Senha);
        if (!r.Succeeded)
        {
            if (r.Errors.Any(e => e.Code == "InvalidToken")) { LinkInvalido = true; return Page(); }
            Erros.AddRange(r.Errors.Select(e => Traduzir(e)));
            return Page();
        }
        if (!user.EmailConfirmed) { user.EmailConfirmed = true; await _um.UpdateAsync(user); }
        TempData["Aviso"] = "Senha criada. Agora é só entrar.";
        return RedirectToPage("/Entrar", new { area = "Admin" });
    }

    private async Task<bool> TokenValidoAsync()
    {
        var user = string.IsNullOrEmpty(Uid) ? null : await _um.FindByIdAsync(Uid);
        var bruto = Decodificar(Token);
        if (user == null || bruto == null) return false;
        return await _um.VerifyUserTokenAsync(user, _um.Options.Tokens.PasswordResetTokenProvider, UserManager<IdentityUser>.ResetPasswordTokenPurpose, bruto);
    }

    private static string? Decodificar(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return null; }
    }

    private static string Traduzir(IdentityError e) => e.Code switch
    {
        "PasswordTooShort" => "A senha precisa ter pelo menos 10 caracteres.",
        "PasswordRequiresDigit" => "Inclua pelo menos um número.",
        "PasswordRequiresLower" => "Inclua pelo menos uma letra minúscula.",
        "PasswordRequiresUpper" => "Inclua pelo menos uma letra maiúscula.",
        _ => "A senha não atende às regras. Tente outra.",
    };
}
