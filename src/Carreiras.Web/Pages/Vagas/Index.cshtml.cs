using Carreiras.Infra.Vitrine;
using Carreiras.Web.Vitrine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages.Vagas;

public class IndexModel : PageModel
{
    private readonly VitrineServico _vitrine;
    public IndexModel(VitrineServico vitrine) => _vitrine = vitrine;

    [BindProperty(SupportsGet = true, Name = "unidade")] public int? Unidade { get; set; }
    [BindProperty(SupportsGet = true, Name = "turno")] public int? Turno { get; set; }
    [BindProperty(SupportsGet = true, Name = "contrato")] public int? Contrato { get; set; }
    [BindProperty(SupportsGet = true, Name = "area")] public int? Area { get; set; }

    public ListagemVagas Listagem { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var filtro = new FiltroPublico(Unidade, Turno, Contrato, Area);
        Listagem = new ListagemVagas
        {
            Pagina = "/Vagas/Index", Filtro = filtro,
            Opcoes = await _vitrine.OpcoesAsync(), Vagas = await _vitrine.ListarVagasAsync(filtro),
        };
    }
}
