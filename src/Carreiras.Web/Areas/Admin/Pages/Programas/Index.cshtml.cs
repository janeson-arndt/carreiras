using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Cadastros;
using Carreiras.Infra.Dados.Entidades;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Programas;

public class IndexModel : PageModel
{
    private readonly CadastroServico _cad;
    public IndexModel(CadastroServico cad) => _cad = cad;

    public List<Programa> Itens { get; private set; } = new();
    public bool PodeEditar => User.IsInRole(Perfis.Administrador);   // RN13

    public async Task OnGetAsync() => Itens = await _cad.ListarProgramasAsync();

    public async Task<IActionResult> OnPostAlternarAsync(int id, bool ativo)
    {
        if (!PodeEditar) return Forbid();
        var r = await _cad.AlternarProgramaAsync(id, ativo);
        if (r.Ok) TempData["Aviso"] = ativo ? "Programa reativado." : "Programa desativado."; else TempData["Erro"] = r.Erro;
        return RedirectToPage();
    }
}
