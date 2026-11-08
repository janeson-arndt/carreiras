using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Usuarios;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Usuarios;

[Authorize(Policy = Politicas.SomenteAdministrador)]
public class NovoModel : PageModel
{
    private readonly GestaoUsuarios _gestao;
    public NovoModel(GestaoUsuarios gestao) => _gestao = gestao;

    // Sem campo de senha, de propósito (RN18).
    [BindProperty] public string Nome { get; set; } = "";
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Perfil { get; set; } = Perfis.Analista;
    public string? Erro { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var r = await _gestao.CadastrarAsync(Nome, Email, Perfil, User.UsuarioRhId(), LinksConvite.Criar(Url, Request));
        if (!r.Ok) { Erro = r.Erro; return Page(); }
        TempData["Aviso"] = "Usuário cadastrado. O convite foi enviado por e-mail.";
        return RedirectToPage("Index");
    }
}
