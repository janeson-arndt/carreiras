using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Carreiras.Infra.Vitrine;

namespace Carreiras.Web.Vitrine;

/// <summary>
/// Marcação schema.org JobPosting (JSON-LD) da página da vaga, para os buscadores de vagas.
/// O JSON sai com o codificador padrão do .NET, que escapa &lt; &gt; &amp; e aspas: seguro dentro de uma tag script.
/// </summary>
public static class JobPostingJsonLd
{
    public const string NomeEmpresa = "Metalúrgica Timboense S.A.";

    private static readonly JsonSerializerOptions Opcoes = new() { Encoder = JavaScriptEncoder.Default, WriteIndented = false };

    /// <summary>Tipo de contrato do cadastro → valor de employmentType do schema.org.</summary>
    public static string TipoEmprego(string contrato) => contrato.Trim().ToLowerInvariant() switch
    {
        "clt" => "FULL_TIME",
        "estágio" or "estagio" => "INTERN",
        "jovem aprendiz" => "INTERN",
        "temporário" or "temporario" => "TEMPORARY",
        _ => "OTHER",
    };

    public static string Criar(VagaPublicaDetalhe v, string urlVaga, string urlLogo)
    {
        var endereco = v.UnidadeEndereco + (string.IsNullOrWhiteSpace(v.UnidadeBairro) ? "" : ", " + v.UnidadeBairro);
        var local = new Dictionary<string, object?>
        {
            ["@type"] = "PostalAddress",
            ["streetAddress"] = endereco,
            ["addressLocality"] = v.UnidadeCidade,
            ["addressRegion"] = v.UnidadeUf,
            ["addressCountry"] = "BR",
        };
        if (!string.IsNullOrWhiteSpace(v.UnidadeCep)) local["postalCode"] = v.UnidadeCep;

        var dados = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "JobPosting",
            ["title"] = v.Titulo,
            ["description"] = DescricaoHtml(v),
            ["identifier"] = new Dictionary<string, object?> { ["@type"] = "PropertyValue", ["name"] = NomeEmpresa, ["value"] = v.Slug },
            ["datePosted"] = (v.PublicadaEm ?? DateTime.UtcNow).ToString("yyyy-MM-dd"),
            ["employmentType"] = TipoEmprego(v.Contrato),
            ["hiringOrganization"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization", ["name"] = NomeEmpresa, ["logo"] = urlLogo,
            },
            ["jobLocation"] = new Dictionary<string, object?> { ["@type"] = "Place", ["address"] = local },
            ["industry"] = "Metalurgia",
            ["occupationalCategory"] = v.Area,
            ["url"] = urlVaga,
        };
        if (v.Modalidade.Equals("Remoto", StringComparison.OrdinalIgnoreCase)) dados["jobLocationType"] = "TELECOMMUTE";

        // RN04: a faixa salarial só entra quando a vaga manda mostrar (a consulta já devolve nulo nos outros casos).
        if (v.SalarioMin.HasValue && v.SalarioMax.HasValue)
            dados["baseSalary"] = new Dictionary<string, object?>
            {
                ["@type"] = "MonetaryAmount", ["currency"] = "BRL",
                ["value"] = new Dictionary<string, object?>
                {
                    ["@type"] = "QuantitativeValue", ["minValue"] = v.SalarioMin.Value, ["maxValue"] = v.SalarioMax.Value, ["unitText"] = "MONTH",
                },
            };

        return JsonSerializer.Serialize(dados, Opcoes);
    }

    private static string DescricaoHtml(VagaPublicaDetalhe v)
    {
        static string P(string t) => "<p>" + WebUtility.HtmlEncode(t).Replace("\n", "<br>") + "</p>";
        static string Ul(IEnumerable<string> itens) => "<ul>" + string.Concat(itens.Select(i => "<li>" + WebUtility.HtmlEncode(i) + "</li>")) + "</ul>";

        var partes = new List<string> { P(v.Descricao) };
        if (v.Atividades.Count > 0) partes.Add("<p><strong>O que você vai fazer</strong></p>" + Ul(v.Atividades));
        var obrig = v.Requisitos.Where(r => r.Obrigatorio).Select(r => r.Texto).ToList();
        if (obrig.Count > 0) partes.Add("<p><strong>O que pedimos</strong></p>" + Ul(obrig));
        var desej = v.Requisitos.Where(r => !r.Obrigatorio).Select(r => r.Texto).ToList();
        if (desej.Count > 0) partes.Add("<p><strong>Seria bom ter</strong></p>" + Ul(desej));
        if (v.Diferenciais.Count > 0) partes.Add("<p><strong>Diferenciais</strong></p>" + Ul(v.Diferenciais));
        if (v.Beneficios.Count > 0) partes.Add("<p><strong>Benefícios</strong></p>" + Ul(v.Beneficios));
        return string.Concat(partes);
    }
}
