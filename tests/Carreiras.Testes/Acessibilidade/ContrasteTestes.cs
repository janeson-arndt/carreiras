using System.Globalization;
using System.Text.RegularExpressions;

namespace Carreiras.Testes.Acessibilidade;

/// <summary>
/// Contraste WCAG AA (4,5:1 para texto, 3:1 para bordas e componentes) calculado a partir do CSS real
/// (wwwroot/css/carreiras.css), para que mudar uma cor sem querer quebre o teste.
/// </summary>
public class ContrasteTestes
{
    private static readonly string Css = File.ReadAllText(Caminho("src/Carreiras.Web/wwwroot/css/carreiras.css"));
    private static readonly Dictionary<string, string> Tokens = Regex.Matches(Css, @"--(c-[a-z-]+):\s*(#[0-9A-Fa-f]{6})")
        .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.OrdinalIgnoreCase);

    private static string Caminho(string relativo)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, relativo))) d = d.Parent;
        return d == null ? throw new FileNotFoundException(relativo) : Path.Combine(d.FullName, relativo);
    }

    private static double Luminancia(string hex)
    {
        double Canal(int i)
        {
            var c = int.Parse(hex.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canal(0) + 0.7152 * Canal(1) + 0.0722 * Canal(2);
    }

    public static double Razao(string a, string b)
    {
        var (la, lb) = (Luminancia(a), Luminancia(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static string Token(string nome) => Tokens[nome];

    /// <summary>Valor de uma propriedade dentro do bloco de um seletor exato.</summary>
    private static string Declarado(string seletor, string propriedade)
    {
        var bloco = Regex.Match(Css, Regex.Escape(seletor) + @"\s*\{([^}]*)\}");
        Assert.True(bloco.Success, $"seletor não encontrado: {seletor}");
        var v = Regex.Match(bloco.Groups[1].Value, propriedade + @":\s*(#[0-9A-Fa-f]{6}|var\(--(c-[a-z-]+)\))");
        Assert.True(v.Success, $"{seletor} não declara {propriedade}");
        return v.Groups[2].Success && v.Groups[2].Length > 0 ? Token(v.Groups[2].Value) : v.Groups[1].Value;
    }

    [Fact]
    public void Tokens_do_briefing_estao_no_css()
    {
        Assert.Equal("#006729", Token("c-verde-escuro"), ignoreCase: true);
        Assert.Equal("#118F40", Token("c-verde"), ignoreCase: true);
        Assert.Equal("#232323", Token("c-texto"), ignoreCase: true);
        Assert.Equal("#B3261E", Token("c-erro"), ignoreCase: true);
    }

    [Theory]
    [InlineData("c-texto", "#FFFFFF")]
    [InlineData("c-texto", "c-fundo")]
    [InlineData("c-cinza-texto", "#FFFFFF")]
    [InlineData("c-cinza-texto", "c-fundo")]
    [InlineData("c-cinza-texto", "c-verde-claro")]
    [InlineData("c-verde-escuro", "#FFFFFF")]
    [InlineData("c-verde-escuro", "c-fundo")]
    [InlineData("c-verde-escuro", "c-verde-claro")]
    [InlineData("c-verde-escuro-hover", "#FFFFFF")]
    [InlineData("c-verde-escuro-hover", "c-verde-claro")]   // iniciais do avatar
    [InlineData("c-erro", "#FFFFFF")]
    [InlineData("c-erro", "c-erro-fundo")]
    public void Texto_dos_tokens_tem_pelo_menos_4_5_para_1(string fg, string bg)
    {
        string Cor(string x) => x.StartsWith('#') ? x : Token(x);
        var r = Razao(Cor(fg), Cor(bg));
        Assert.True(r >= 4.5, $"{fg} sobre {bg}: {r:F2}:1");
    }

    [Theory]
    [InlineData("#FFFFFF", "c-verde-escuro")]          // texto do botão primário
    [InlineData("#FFFFFF", "c-verde-escuro-hover")]    // botão primário em hover/ativo
    public void Texto_branco_sobre_botao_primario_passa(string fg, string bg) =>
        Assert.True(Razao(fg, Token(bg)) >= 4.5);

    [Fact]
    public void Verde_claro_do_briefing_nao_serve_para_texto_pequeno()
    {
        // Documenta a decisão do briefing: #118F40 tem 4,18:1 sobre branco. Não pode ser usado como cor de texto.
        var r = Razao(Token("c-verde"), "#FFFFFF");
        Assert.InRange(r, 4.0, 4.5);
        Assert.DoesNotMatch(@"(?<![-a-z])color:\s*var\(--c-verde\)", Css);
        Assert.DoesNotMatch(@"(?<![-a-z])color:\s*#118F40", Css);
    }

    [Theory]
    [InlineData("c-cinza")]
    public void Bordas_de_campos_tem_pelo_menos_3_para_1_sobre_branco(string token) =>
        Assert.True(Razao(Token(token), "#FFFFFF") >= 3.0);

    [Theory]
    [InlineData(".s-RASCUNHO")]
    [InlineData(".s-PUBLICADA")]
    [InlineData(".s-PAUSADA")]
    [InlineData(".s-ENCERRADA")]
    [InlineData(".aviso-erro")]
    [InlineData(".aviso-ok")]
    public void Componentes_com_cores_proprias_passam(string seletor)
    {
        // selos de situação do protótipo (Rascunho, Publicada, Pausada, Encerrada) e avisos
        var cor = Declarado(seletor, "color:?");
        var fundo = Declarado(seletor, "background");
        var r = Razao(cor, fundo);
        Assert.True(r >= 4.5, $"{seletor}: {cor} sobre {fundo} = {r:F2}:1");
    }

    [Fact]
    public void Texto_de_apoio_do_menu_e_dicas_usam_o_cinza_de_texto()
    {
        // textos de apoio (.ajuda, .text-secundario, subtítulo, marca) usam o cinza de texto (#5C5C5C), não o de bordas (#717171)
        Assert.Equal(Token("c-cinza-texto"), Declarado(".text-secundario, .ajuda", "color"), ignoreCase: true);
        Assert.Equal(Token("c-cinza-texto"), Declarado(".topo-pagina p", "color"), ignoreCase: true);
        Assert.Equal(Token("c-cinza-texto"), Declarado(".admin-marca small", "color"), ignoreCase: true);
        Assert.Equal(Token("c-cinza-texto"), Declarado(".conta-txt small", "color"), ignoreCase: true);
    }
}
