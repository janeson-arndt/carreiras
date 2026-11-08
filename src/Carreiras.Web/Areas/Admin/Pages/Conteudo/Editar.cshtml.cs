using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Cadastros;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Conteudo;

public class EditarModel : PageModel
{
    private readonly ConteudoServico _svc;
    public EditarModel(ConteudoServico svc) => _svc = svc;

    [BindProperty] public ConteudoForm Form { get; set; } = new();
    public Dictionary<string, string> Erros { get; private set; } = new();
    public bool PodeEditar => User.IsInRole(Perfis.Administrador);   // RN13
    public bool Existente => Form.Id is not null;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            var f = await _svc.ObterAsync(id.Value);
            if (f == null) return NotFound();
            Form = f;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (!PodeEditar) return Forbid();
        Form.Id = id;
        if (id is not null)
        {
            // a seção de um conteúdo existente não muda: o campo vem do banco, não do formulário
            var atual = await _svc.ObterAsync(id.Value);
            if (atual == null) return NotFound();
            Form.Secao = atual.Secao;
        }
        var (r, _, erros) = await _svc.SalvarAsync(Form);
        if (r.Ok)
        {
            TempData["Aviso"] = id is null ? "Conteúdo criado." : "Conteúdo salvo.";
            return RedirectToPage("Index");
        }
        Erros = erros;
        return Page();
    }

    public string Erro(string c) => Erros.TryGetValue(c, out var m) ? m : "";
    public string Invalido(string c) => Erros.ContainsKey(c) ? "true" : "false";
}
