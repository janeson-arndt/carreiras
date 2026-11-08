using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Usuarios;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Usuarios;

[Authorize(Policy = Politicas.SomenteAdministrador)]
public class EditarModel : PageModel
{
    private readonly GestaoUsuarios _gestao;
    public EditarModel(GestaoUsuarios gestao) => _gestao = gestao;

    public UsuarioItem? Usuario { get; private set; }
    public bool Proprio => Usuario != null && Usuario.Id == User.UsuarioRhId();
    public string? Erro { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Usuario = (await _gestao.ListarAsync()).SingleOrDefault(u => u.Id == id);
        return Usuario == null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, string perfil)
    {
        var r = await _gestao.AlterarPerfilAsync(id, perfil, User.UsuarioRhId());
        if (!r.Ok)
        {
            Erro = r.Erro;
            Usuario = (await _gestao.ListarAsync()).SingleOrDefault(u => u.Id == id);
            return Usuario == null ? NotFound() : Page();
        }
        TempData["Aviso"] = "Perfil atualizado. A pessoa precisa entrar de novo.";
        return RedirectToPage("Index");
    }
}
