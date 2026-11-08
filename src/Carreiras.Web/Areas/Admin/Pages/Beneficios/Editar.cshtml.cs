using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Cadastros;
using Carreiras.Infra.Dados.Entidades;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Beneficios;

public class EditarModel : PageModel
{
    private readonly CadastroServico _cad;
    public EditarModel(CadastroServico cad) => _cad = cad;

    [BindProperty] public BeneficioForm Form { get; set; } = new();
    public List<BeneficioCategoria> Categorias { get; private set; } = new();
    public Dictionary<string, string> Erros { get; private set; } = new();
    public bool PodeEditar => User.IsInRole(Perfis.Administrador);   // RN13

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            var f = await _cad.ObterBeneficioAsync(id.Value);
            if (f == null) return NotFound();
            Form = f;
        }
        Categorias = await _cad.CategoriasAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (!PodeEditar) return Forbid();
        Form.Id = id;
        var (r, _, erros) = await _cad.SalvarBeneficioAsync(Form);
        if (r.Ok)
        {
            TempData["Aviso"] = id is null ? "Benefício criado." : "Benefício salvo.";
            return RedirectToPage("Index");
        }
        Erros = erros;
        Categorias = await _cad.CategoriasAsync();
        return Page();
    }

    public string Erro(string c) => Erros.TryGetValue(c, out var m) ? m : "";
    public string Invalido(string c) => Erros.ContainsKey(c) ? "true" : "false";
}
