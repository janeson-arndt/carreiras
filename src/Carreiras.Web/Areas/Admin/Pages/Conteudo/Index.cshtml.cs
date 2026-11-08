using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Cadastros;
using Carreiras.Infra.Dados.Entidades;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Conteudo;

public class IndexModel : PageModel
{
    private readonly ConteudoServico _svc;
    public IndexModel(ConteudoServico svc) => _svc = svc;

    public List<ConteudoInstitucional> Itens { get; private set; } = new();
    public bool PodeEditar => User.IsInRole(Perfis.Administrador);   // RN13

    public async Task OnGetAsync() => Itens = await _svc.ListarAsync();

    public async Task<IActionResult> OnPostPublicarAsync(int id, bool publicado)
    {
        if (!PodeEditar) return Forbid();
        var r = await _svc.AlternarPublicadoAsync(id, publicado);
        if (r.Ok) TempData["Aviso"] = publicado ? "Conteúdo publicado." : "Conteúdo despublicado. Ele sai do portal público."; else TempData["Erro"] = r.Erro;
        return RedirectToPage();
    }
}
