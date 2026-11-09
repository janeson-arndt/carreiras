using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Candidaturas;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Candidaturas;

public class IndexModel : PageModel
{
    private readonly CandidaturaRhServico _servico;
    public IndexModel(CandidaturaRhServico servico) => _servico = servico;

    /// <summary>"" = todas, "espontanea" = banco de talentos, ou o id da vaga.</summary>
    [BindProperty(SupportsGet = true)] public string? Vaga { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public List<CandidaturaLinha> Itens { get; private set; } = new();
    public int Total { get; private set; }
    public List<(int Id, string Titulo, int Candidaturas)> Vagas { get; private set; } = new();
    public int Espontaneas { get; private set; }

    public bool PodeGerenciar => User.IsInRole(Perfis.Administrador) || User.IsInRole(Perfis.Analista);
    public bool FiltroAtivo => !string.IsNullOrEmpty(Vaga) || !string.IsNullOrEmpty(Status);
    public int Paginas => Math.Max(1, (int)Math.Ceiling(Total / (double)CandidaturaRhServico.PorPagina));

    public async Task OnGetAsync(CancellationToken ct)
    {
        var espontanea = string.Equals(Vaga, "espontanea", StringComparison.Ordinal);
        int? vagaId = int.TryParse(Vaga, out var v) ? v : null;
        (Itens, Total) = await _servico.ListarAsync(new FiltroCandidaturas(vagaId, espontanea, Status), Pagina, ct);
        (Vagas, Espontaneas) = await _servico.OpcoesFiltroAsync(ct);
    }

    // O servidor exige perfil que gerencia (LEITURA recebe 403), não só o seletor desabilitado.
    public async Task<IActionResult> OnPostStatusAsync(int id, string novo, CancellationToken ct)
    {
        if (!PodeGerenciar) return Forbid();
        var (ok, erro) = await _servico.MudarStatusAsync(id, novo, User.UsuarioRhId(), ct);
        if (ok) TempData["Aviso"] = "Situação atualizada."; else TempData["Erro"] = erro;
        return RedirectToPage(new { Vaga, Status, Pagina });
    }
}
