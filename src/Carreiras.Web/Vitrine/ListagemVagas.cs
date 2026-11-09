using Carreiras.Infra.Vitrine;

namespace Carreiras.Web.Vitrine;

/// <summary>Dados da lista de vagas com filtros, compartilhada pelo início ("/") e por "/vagas".</summary>
public sealed class ListagemVagas
{
    public string Pagina { get; init; } = "/Vagas/Index";
    public FiltroPublico Filtro { get; init; } = new();
    public OpcoesFiltro Opcoes { get; init; } = new(new(), new(), new(), new());
    public List<VagaPublicaResumo> Vagas { get; init; } = new();

    /// <summary>Os filtros que valem ao mexer em um grupo: todos os outros, para um clique não apagar os demais.</summary>
    public IEnumerable<KeyValuePair<string, string>> Outros(string grupo)
    {
        if (grupo != "unidade" && Filtro.UnidadeId.HasValue) yield return new("unidade", Filtro.UnidadeId.Value.ToString());
        if (grupo != "turno" && Filtro.TurnoId.HasValue) yield return new("turno", Filtro.TurnoId.Value.ToString());
        if (grupo != "contrato" && Filtro.TipoContratoId.HasValue) yield return new("contrato", Filtro.TipoContratoId.Value.ToString());
        if (grupo != "area" && Filtro.AreaId.HasValue) yield return new("area", Filtro.AreaId.Value.ToString());
    }
}
