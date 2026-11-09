using System.Globalization;
using Carreiras.Infra.Vitrine;
using Carreiras.Web.Vitrine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages.Vagas;

public class DetalheModel : PageModel
{
    private static readonly CultureInfo Br = new("pt-BR");
    private readonly VitrineServico _vitrine;
    public DetalheModel(VitrineServico vitrine) => _vitrine = vitrine;

    public VagaPublicaDetalhe? Vaga { get; private set; }
    public string? JsonLd { get; private set; }

    /// <summary>Vaga que não existe, ou que não está PUBLICADA (RN03), vira a página "Vaga não encontrada" com status 404.</summary>
    public async Task<IActionResult> OnGetAsync(string slug)
    {
        Vaga = await _vitrine.ObterVagaAsync(slug);
        if (Vaga == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }
        var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        JsonLd = JobPostingJsonLd.Criar(Vaga, $"{baseUrl}/vagas/{Vaga.Slug}", $"{baseUrl}/img/logo.png");
        return Page();
    }

    public static string Dinheiro(decimal v) => string.Format(Br, v % 1 == 0 ? "R$ {0:N0}" : "R$ {0:N2}", v);
}
