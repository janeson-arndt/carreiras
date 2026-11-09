using AngleSharp;
using AngleSharp.Dom;

namespace Carreiras.Testes.Acessibilidade;

/// <summary>Controles negativos: prova que o verificador de estrutura reprova defeitos reais (não é um teste que sempre passa).</summary>
public class VerificadorTestes
{
    private static async Task<List<string>> Problemas(string html)
    {
        IDocument doc = await BrowsingContext.New(Configuration.Default).OpenAsync(r => r.Content(html));
        return EstruturaTestes.Verificar(doc);
    }

    private const string Bom = @"<!DOCTYPE html><html lang=""pt-BR""><head><meta name=""viewport"" content=""width=device-width""><title>Ok</title></head>
<body><a class=""visually-hidden-focusable"" href=""#c"">Ir</a><nav aria-label=""Menu""><a href=""/x"">X</a></nav>
<main id=""c""><h1>T</h1><form method=""post""><input type=""hidden"" name=""__RequestVerificationToken"" value=""t"">
<label for=""a"">Nome</label><input id=""a"" name=""a""><button type=""submit"">Salvar</button></form></main></body></html>";

    [Fact]
    public async Task Pagina_correta_nao_tem_problemas() => Assert.Empty(await Problemas(Bom));

    [Theory]
    [InlineData("lang=\"pt-BR\"", "lang=\"en\"", "lang")]
    [InlineData("<title>Ok</title>", "<title></title>", "title")]
    [InlineData("<h1>T</h1>", "<h1>T</h1><h1>U</h1>", "h1")]
    [InlineData("<label for=\"a\">Nome</label>", "", "sem rótulo")]
    [InlineData("<label for=\"a\">Nome</label>", "<label for=\"zzz\">Nome</label>", "inexistente")]
    [InlineData("<button type=\"submit\">Salvar</button>", "<button type=\"submit\"></button>", "botão sem nome")]
    [InlineData("<nav aria-label=\"Menu\">", "<nav>", "nav sem aria-label")]
    [InlineData("<input type=\"hidden\" name=\"__RequestVerificationToken\" value=\"t\">", "", "antiforgery")]
    [InlineData("<main id=\"c\">", "<main id=\"outro\">", "Ir para o conteúdo")]
    [InlineData("<a href=\"/x\">X</a>", "<a href=\"/x\"></a>", "link sem nome")]
    public async Task Verificador_reprova_defeito(string de, string para, string trecho)
    {
        var p = await Problemas(Bom.Replace(de, para));
        Assert.Contains(p, m => m.Contains(trecho, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reprova_aside_sem_nome_ou_repetido_e_aceita_nomes_distintos()
    {
        Assert.Contains(await Problemas(Bom.Replace("</main>", "<aside>a</aside></main>")), m => m.Contains("aside sem aria-label"));
        Assert.Contains(await Problemas(Bom.Replace("</main>", "<aside aria-label=\"X\">a</aside><aside aria-label=\"x\">b</aside></main>")), m => m.Contains("nome repetido"));
        Assert.Empty(await Problemas(Bom.Replace("</main>", "<aside aria-label=\"X\">a</aside><aside aria-label=\"Y\">b</aside></main>")));
    }

    [Fact]
    public async Task Aceita_rotulo_por_envolvimento_mas_reprova_label_envolvente_vazio()
    {
        Assert.Empty(await Problemas(Bom.Replace("</main>", "<label><input type=\"radio\" name=\"p\" /> Opção</label></main>")));
        var ruim = await Problemas(Bom.Replace("</main>", "<label><input type=\"radio\" name=\"p\" /></label></main>"));
        Assert.Contains(ruim, m => m.Contains("campo sem rótulo"));
    }

    [Fact]
    public async Task Reprova_tabela_sem_legenda_e_th_sem_scope() =>
        Assert.Contains("tabela sem <caption>", await Problemas(Bom.Replace("</main>", "<table><tr><th>A</th></tr></table></main>")));

    [Fact]
    public async Task Reprova_th_sem_scope() =>
        Assert.Contains(await Problemas(Bom.Replace("</main>", "<table><caption>c</caption><tr><th>A</th></tr></table></main>")), m => m.StartsWith("th sem scope"));

    [Fact]
    public async Task Reprova_referencia_aria_quebrada_e_id_duplicado()
    {
        var p = await Problemas(Bom.Replace("</main>", "<p id=\"d\">1</p><p id=\"d\">2</p><button type=\"button\" data-acoes aria-controls=\"nada\">M</button></main>"));
        Assert.Contains(p, m => m.Contains("id duplicado"));
        Assert.Contains(p, m => m.Contains("aria-controls aponta para id inexistente"));
        Assert.Contains(p, m => m.Contains("sem aria-expanded"));
    }

    [Fact]
    public async Task Reprova_acao_bloqueada_sem_motivo_e_campo_invalido_sem_mensagem()
    {
        var p = await Problemas(Bom.Replace("</main>",
            "<button type=\"submit\" aria-disabled=\"true\">Desativar</button><label for=\"b\">B</label><input id=\"b\" aria-invalid=\"true\"></main>"));
        Assert.Contains(p, m => m.Contains("sem o motivo escrito"));
        Assert.Contains(p, m => m.Contains("campo inválido sem mensagem"));
    }

    [Fact]
    public async Task Reprova_area_rolavel_sem_teclado_mas_aceita_a_correta_e_a_sem_rolagem()
    {
        var ruim = await Problemas(Bom.Replace("</main>", "<div class=\"table-responsive\"></div></main>"));
        Assert.Contains(ruim, m => m.Contains("área rolável"));
        Assert.Empty(await Problemas(Bom.Replace("</main>", "<div class=\"table-responsive\" tabindex=\"0\" role=\"region\" aria-label=\"Tabela\"></div></main>")));
        Assert.Empty(await Problemas(Bom.Replace("</main>", "<div class=\"table-responsive\" style=\"overflow: visible\"></div></main>")));
    }

    [Fact]
    public async Task Reprova_imagem_sem_alt_e_titulo_pulando_nivel()
    {
        var p = await Problemas(Bom.Replace("</main>", "<h3>Pulou</h3><img src=\"a.png\"></main>"));
        Assert.Contains(p, m => m.Contains("img sem alt"));
        Assert.Contains(p, m => m.Contains("pula de h1 para h3"));
    }
}
