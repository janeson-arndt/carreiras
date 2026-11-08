using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Auditoria;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Auditoria;

[Authorize(Policy = Politicas.SomenteAdministrador)]   // RN13
public class IndexModel : PageModel
{
    private readonly AuditoriaConsulta _consulta;
    public IndexModel(AuditoriaConsulta consulta) => _consulta = consulta;

    [BindProperty(SupportsGet = true)] public string? Tabela { get; set; }
    [BindProperty(SupportsGet = true)] public int? Usuario { get; set; }
    [BindProperty(SupportsGet = true)] public int? Registro { get; set; }
    [BindProperty(SupportsGet = true)] public string? Campo { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? De { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? Ate { get; set; }
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;

    public PaginaAuditoria Resultado { get; private set; } = new(new(), 0, 1, AuditoriaConsulta.TamanhoPadrao);
    public List<string> Tabelas { get; private set; } = new();
    public List<(int Id, string Nome)> Usuarios { get; private set; } = new();
    public string? ErroPeriodo { get; private set; }

    public async Task OnGetAsync()
    {
        Tabelas = await _consulta.TabelasAsync();
        Usuarios = await _consulta.UsuariosAsync();
        if (De.HasValue && Ate.HasValue && Ate < De)
        {
            ErroPeriodo = "A data final não pode ser anterior à inicial.";
            return;
        }
        Resultado = await _consulta.ConsultarAsync(new FiltroAuditoria(
            string.IsNullOrWhiteSpace(Tabela) ? null : Tabela, Usuario, Registro, De, Ate, Campo), Pagina);
    }

    /// <summary>Mantém os filtros ao trocar de página.</summary>
    public Dictionary<string, string?> Rota(int pagina) => new()
    {
        ["Tabela"] = Tabela, ["Usuario"] = Usuario?.ToString(), ["Registro"] = Registro?.ToString(), ["Campo"] = Campo,
        ["De"] = De?.ToString("yyyy-MM-dd"), ["Ate"] = Ate?.ToString("yyyy-MM-dd"), ["Pagina"] = pagina.ToString(),
    };
}
