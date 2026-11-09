using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Candidaturas;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Candidaturas;

public class DetalheModel : PageModel
{
    private readonly CandidaturaRhServico _servico;
    private readonly ILogger<DetalheModel> _log;
    public DetalheModel(CandidaturaRhServico servico, ILogger<DetalheModel> log) { _servico = servico; _log = log; }

    public CandidaturaDetalhe? Candidatura { get; private set; }
    public bool PodeGerenciar => User.IsInRole(Perfis.Administrador) || User.IsInRole(Perfis.Analista);

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        Candidatura = await _servico.ObterAsync(id, ct);
        return Candidatura == null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, string novo, CancellationToken ct)
    {
        if (!PodeGerenciar) return Forbid();
        var (ok, erro) = await _servico.MudarStatusAsync(id, novo, User.UsuarioRhId(), ct);
        if (ok) TempData["Aviso"] = "Situação atualizada."; else TempData["Erro"] = erro;
        return RedirectToPage(new { id });
    }

    /// <summary>
    /// Entrega o currículo só a usuário do RH autenticado (a pasta área Admin exige login). Vai como anexo, com o tipo
    /// correto e sem cache. O log registra quem abriu qual candidatura, sem nome nem e-mail do candidato.
    /// </summary>
    public async Task<IActionResult> OnGetCurriculoAsync(int id, CancellationToken ct)
    {
        var arq = await _servico.AbrirCurriculoAsync(id, ct);
        if (arq == null) return NotFound();
        _log.LogInformation("Currículo da candidatura {CandidaturaId} aberto pelo usuário do RH {UsuarioRhId}", id, User.UsuarioRhId());
        Response.Headers.CacheControl = "no-store";
        return File(arq.Conteudo, arq.ContentType, arq.NomeExibicao);
    }
}
