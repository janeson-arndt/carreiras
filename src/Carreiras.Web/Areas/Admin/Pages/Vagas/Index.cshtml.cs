using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Vagas;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Web.Areas.Admin.Pages.Vagas;

public class IndexModel : PageModel
{
    private readonly VagaServico _vagas;
    private readonly AppDbContext _db;
    public IndexModel(VagaServico vagas, AppDbContext db) { _vagas = vagas; _db = db; }

    [BindProperty(SupportsGet = true)] public string? Busca { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int? UnidadeId { get; set; }

    public List<VagaLinha> Itens { get; private set; } = new();
    public List<Unidade> Unidades { get; private set; } = new();
    public bool PodeGerenciar => User.IsInRole(Perfis.Administrador) || User.IsInRole(Perfis.Analista);

    public async Task OnGetAsync()
    {
        Unidades = await _db.Unidades.AsNoTracking().OrderBy(u => u.Nome).ToListAsync();
        Itens = await _vagas.ListarAsync(new FiltroVagas(Busca, Status, UnidadeId));
    }

    // As ações exigem GerenciarVagas no servidor (LEITURA recebe 403), não só botão escondido.
    public async Task<IActionResult> OnPostStatusAsync(int id, string novo)
    {
        if (!PodeGerenciar) return Forbid();
        var r = await _vagas.MudarStatusAsync(id, novo, User.UsuarioRhId());
        if (r.Ok) TempData["Aviso"] = "Situação da vaga atualizada."; else TempData["Erro"] = r.Erro;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDuplicarAsync(int id)
    {
        if (!PodeGerenciar) return Forbid();
        var (r, novoId) = await _vagas.DuplicarAsync(id, User.UsuarioRhId());
        if (!r.Ok) { TempData["Erro"] = r.Erro; return RedirectToPage(); }
        TempData["Aviso"] = "Vaga duplicada como rascunho.";
        return RedirectToPage("Editar", new { id = novoId });
    }
}
