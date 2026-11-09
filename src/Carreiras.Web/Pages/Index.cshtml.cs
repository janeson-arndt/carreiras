using Carreiras.Infra.Vitrine;
using Carreiras.Web.Vitrine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages;

public class IndexModel : PageModel
{
    private readonly VitrineServico _vitrine;
    public IndexModel(VitrineServico vitrine) => _vitrine = vitrine;

    [BindProperty(SupportsGet = true, Name = "unidade")] public int? Unidade { get; set; }
    [BindProperty(SupportsGet = true, Name = "turno")] public int? Turno { get; set; }
    [BindProperty(SupportsGet = true, Name = "contrato")] public int? Contrato { get; set; }
    [BindProperty(SupportsGet = true, Name = "area")] public int? Area { get; set; }

    public ListagemVagas Listagem { get; private set; } = new();
    public List<UnidadePublica> Unidades { get; private set; } = new();
    public List<CategoriaBeneficios> Categorias { get; private set; } = new();
    public List<ProgramaPublico> Programas { get; private set; } = new();
    public Dictionary<string, ConteudoPublico> Conteudos { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var filtro = new FiltroPublico(Unidade, Turno, Contrato, Area);
        Listagem = new ListagemVagas
        {
            Pagina = "/Index", Filtro = filtro,
            Opcoes = await _vitrine.OpcoesAsync(), Vagas = await _vitrine.ListarVagasAsync(filtro),
        };
        Unidades = await _vitrine.UnidadesAsync();
        Categorias = await _vitrine.BeneficiosPorCategoriaAsync();
        Programas = await _vitrine.ProgramasAsync();
        Conteudos = (await _vitrine.ConteudosAsync()).ToDictionary(c => c.Secao, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Texto do cadastro com quebras de linha: cada linha vira um parágrafo.</summary>
    public static IEnumerable<string> Paragrafos(string texto) =>
        texto.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
