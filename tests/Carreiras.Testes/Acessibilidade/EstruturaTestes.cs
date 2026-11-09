using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Carreiras.Dominio.Usuarios;
using Carreiras.Testes.Integracao;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carreiras.Testes.Acessibilidade;

/// <summary>
/// Verificação automática das telas do portal do RH: idioma, título, marcos, rótulos em todos os campos,
/// tabelas com legenda e cabeçalhos, nomes acessíveis em botões e links, ids únicos e referências ARIA válidas.
/// (O contraste está em ContrasteTestes; o axe-core no navegador cobre o que depende de layout.)
/// </summary>
[Collection("banco")]
public class EstruturaTestes : IDisposable
{
    private const string Senha = "SenhaForte2026x";
    private readonly BancoTeste _b;
    private readonly WebApplicationFactory<Program> _fabrica;

    public EstruturaTestes(BancoTeste b)
    {
        _b = b;
        Environment.SetEnvironmentVariable("ConnectionStrings__Carreiras", b.ConexaoApp);
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(h => h.UseEnvironment("Development"));
    }

    public void Dispose() => _fabrica.Dispose();

    private HttpClient Novo() => _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> AdminAsync()
    {
        var email = $"acess-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Administrador, email: email);
        var c = Novo();
        var html = await (await c.GetAsync("/admin/entrar")).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var r = await c.PostAsync("/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = token, ["Email"] = email, ["Senha"] = Senha }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return c;
    }

    private static async Task<IDocument> Abrir(string html) =>
        await BrowsingContext.New(Configuration.Default).OpenAsync(r => r.Content(html));

    private async Task<List<string>> Problemas(HttpClient c, string url)
    {
        var resp = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await Abrir(await resp.Content.ReadAsStringAsync());
        return Verificar(doc);
    }

    public static List<string> Verificar(IDocument doc)
    {
        var p = new List<string>();
        void Falha(string m) => p.Add(m);

        if (doc.DocumentElement.GetAttribute("lang") != "pt-BR") Falha("html sem lang=\"pt-BR\"");
        if (string.IsNullOrWhiteSpace(doc.Title)) Falha("página sem <title>");
        if (doc.QuerySelectorAll("h1").Length != 1) Falha($"deve haver exatamente um h1 (há {doc.QuerySelectorAll("h1").Length})");
        if (doc.QuerySelector("meta[name=viewport]") == null) Falha("sem meta viewport");
        if (doc.QuerySelector("main") == null) Falha("sem landmark <main>");

        // ids únicos
        foreach (var g in doc.QuerySelectorAll("[id]").GroupBy(e => e.Id).Where(g => g.Count() > 1))
            Falha($"id duplicado: {g.Key}");
        var ids = doc.QuerySelectorAll("[id]").Select(e => e.Id).ToHashSet();

        // link de pular para o conteúdo
        var pular = doc.QuerySelector("a[href^='#'].visually-hidden-focusable");
        if (doc.QuerySelector("nav") != null && (pular == null || !ids.Contains(pular.GetAttribute("href")!.TrimStart('#'))))
            Falha("falta o link 'Ir para o conteúdo' apontando para um id existente");

        // landmarks de navegação precisam de nome
        foreach (var n in doc.QuerySelectorAll("nav"))
            if (string.IsNullOrWhiteSpace(n.GetAttribute("aria-label")) && string.IsNullOrWhiteSpace(n.GetAttribute("aria-labelledby")))
                Falha("nav sem aria-label");

        // marcos "complementares" (<aside>) precisam de nome próprio e único (axe: landmark-unique)
        var asides = doc.QuerySelectorAll("aside").ToList();
        if (asides.Count > 0)
        {
            var nomes = asides.Select(a => (a.GetAttribute("aria-label") ?? "").Trim()).ToList();
            if (nomes.Any(string.IsNullOrEmpty)) Falha("aside sem aria-label");
            else if (nomes.Distinct(StringComparer.CurrentCultureIgnoreCase).Count() != nomes.Count) Falha("aside com nome repetido");
        }

        // campos de formulário: todos com rótulo
        foreach (var campo in doc.QuerySelectorAll("input, select, textarea"))
        {
            var tipo = (campo.GetAttribute("type") ?? "text").ToLowerInvariant();
            if (tipo is "hidden" or "submit" or "button") continue;
            var id = campo.Id;
            // rótulo por "for" ou por envolvimento (<label><input> texto</label>), os dois dão nome acessível
            var temLabel = (!string.IsNullOrEmpty(id) && doc.QuerySelector($"label[for='{id}']") != null)
                           || (campo.Closest("label") is { } envolvente && !string.IsNullOrWhiteSpace(envolvente.TextContent));
            var temAria = !string.IsNullOrWhiteSpace(campo.GetAttribute("aria-label")) || !string.IsNullOrWhiteSpace(campo.GetAttribute("aria-labelledby"));
            if (!temLabel && !temAria) Falha($"campo sem rótulo: <{campo.TagName.ToLower()} name={campo.GetAttribute("name")} id={id}>");
        }
        foreach (var l in doc.QuerySelectorAll("label[for]"))
            if (!ids.Contains(l.GetAttribute("for")!)) Falha($"label aponta para id inexistente: {l.GetAttribute("for")}");

        // botões e links com nome acessível
        foreach (var b in doc.QuerySelectorAll("button"))
            if (string.IsNullOrWhiteSpace(b.TextContent) && string.IsNullOrWhiteSpace(b.GetAttribute("aria-label")))
                Falha("botão sem nome");
        foreach (var a in doc.QuerySelectorAll("a[href]"))
            if (string.IsNullOrWhiteSpace(a.TextContent) && string.IsNullOrWhiteSpace(a.GetAttribute("aria-label")))
                Falha($"link sem nome: {a.GetAttribute("href")}");

        // tabelas: legenda e cabeçalhos com scope
        foreach (var t in doc.QuerySelectorAll("table"))
        {
            if (t.QuerySelector("caption") == null) Falha("tabela sem <caption>");
            foreach (var th in t.QuerySelectorAll("th"))
                if (string.IsNullOrEmpty(th.GetAttribute("scope"))) Falha($"th sem scope: {th.TextContent.Trim()}");
        }

        // área rolável precisa ser alcançável pelo teclado e ter nome (axe: scrollable-region-focusable).
        // As tabelas com menu de ações usam overflow visível (o menu precisa sair da caixa) e não rolam.
        foreach (var r in doc.QuerySelectorAll(".table-responsive"))
        {
            var visivel = (r.GetAttribute("style") ?? "").Replace(" ", "").Contains("overflow:visible");
            if (visivel) continue;
            if (r.GetAttribute("tabindex") != "0" || r.GetAttribute("role") != "region" || string.IsNullOrWhiteSpace(r.GetAttribute("aria-label")))
                Falha("área rolável (.table-responsive) sem tabindex=0, role=region e aria-label");
        }

        // ARIA: referências precisam existir
        foreach (var e in doc.QuerySelectorAll("[aria-controls], [aria-describedby], [aria-labelledby]"))
            foreach (var attr in new[] { "aria-controls", "aria-describedby", "aria-labelledby" })
            {
                var v = e.GetAttribute(attr);
                if (string.IsNullOrWhiteSpace(v)) continue;
                foreach (var alvo in v.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (!ids.Contains(alvo)) Falha($"{attr} aponta para id inexistente: {alvo}");
            }

        // menus de ação (disclosure): aria-expanded e aria-controls
        foreach (var m in doc.QuerySelectorAll("[data-acoes]"))
        {
            if (m.GetAttribute("aria-expanded") is not ("true" or "false")) Falha("menu de ações sem aria-expanded");
            if (string.IsNullOrEmpty(m.GetAttribute("aria-controls"))) Falha("menu de ações sem aria-controls");
            if (m.GetAttribute("type") != "button") Falha("botão de menu sem type=button");
        }
        foreach (var l in doc.QuerySelectorAll(".acoes-lista"))
            if (!l.HasAttribute("hidden")) Falha("lista de ações deve começar oculta");

        // item bloqueado: aria-disabled com o motivo escrito
        foreach (var b in doc.QuerySelectorAll("button[aria-disabled='true']"))
        {
            var d = b.GetAttribute("aria-describedby");
            if (string.IsNullOrEmpty(d) || string.IsNullOrWhiteSpace(doc.GetElementById(d)?.TextContent))
                Falha("ação bloqueada sem o motivo escrito (aria-describedby)");
        }

        // avisos de erro anunciados; campo com aria-invalid="true" tem mensagem
        foreach (var e in doc.QuerySelectorAll(".aviso-erro"))
            if (e.GetAttribute("role") is not ("alert" or "status")) Falha("aviso de erro sem role=alert/status");
        foreach (var c in doc.QuerySelectorAll("[aria-invalid='true']"))
        {
            var d = c.GetAttribute("aria-describedby");
            var texto = string.Join(" ", (d ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(i => doc.GetElementById(i)?.TextContent ?? ""));
            if (string.IsNullOrWhiteSpace(texto)) Falha($"campo inválido sem mensagem: {c.Id}");
        }

        // contagens e avisos dinâmicos
        foreach (var e in doc.QuerySelectorAll("form[method=post]"))
            if (e.QuerySelector("input[name=__RequestVerificationToken]") == null) Falha("formulário POST sem antiforgery");

        // imagens com texto alternativo
        foreach (var img in doc.QuerySelectorAll("img"))
            if (!img.HasAttribute("alt")) Falha("img sem alt");

        // ordem de títulos: não pular nível
        var niveis = doc.QuerySelectorAll("h1,h2,h3,h4,h5,h6").Select(h => int.Parse(h.TagName.Substring(1))).ToList();
        for (var i = 1; i < niveis.Count; i++)
            if (niveis[i] > niveis[i - 1] + 1) Falha($"título pula de h{niveis[i - 1]} para h{niveis[i]}");

        return p;
    }

    public static IEnumerable<object[]> Telas() => new[]
    {
        "/admin/vagas", "/admin/vagas?status=PUBLICADA", "/admin/vagas?busca=zzzz-nada", "/admin/vagas/editar", "/admin/vagas/editar/1",
        "/admin/usuarios", "/admin/usuarios?q=zzzz-nada", "/admin/usuarios/novo",
        "/admin/beneficios", "/admin/beneficios/editar", "/admin/beneficios/editar/1",
        "/admin/programas", "/admin/programas/editar", "/admin/programas/editar/1",
        "/admin/conteudo", "/admin/conteudo/editar", "/admin/conteudo/editar/1",
        "/admin/auditoria", "/admin/auditoria?Tabela=Vaga", "/admin/auditoria?De=2026-12-31&Ate=2026-01-01",
        "/admin/acesso-negado",
    }.Select(u => new object[] { u });

    [Theory]
    [MemberData(nameof(Telas))]
    public async Task Tela_do_portal_do_RH_nao_tem_problemas_de_estrutura(string url)
    {
        // garante registros para as telas /editar/1 (vaga, usuário, benefício, programa, conteúdo)
        var c = await AdminAsync();
        var problemas = await Problemas(c, url);
        Assert.True(problemas.Count == 0, $"{url}:\n - " + string.Join("\n - ", problemas));
    }

    [Theory]
    [InlineData("/admin/entrar")]
    [InlineData("/admin/definir-senha?uid=x&token=y")]
    public async Task Telas_publicas_de_acesso_nao_tem_problemas_de_estrutura(string url)
    {
        var problemas = await Problemas(Novo(), url);
        Assert.True(problemas.Count == 0, $"{url}:\n - " + string.Join("\n - ", problemas));
    }

    [Fact]
    public async Task Erros_de_validacao_ficam_ligados_aos_campos_e_anunciados()
    {
        var c = await AdminAsync();
        var html = await (await c.GetAsync("/admin/conteudo/editar")).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var r = await c.PostAsync("/admin/conteudo/editar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = token, ["Form.Secao"] = "", ["Form.Titulo"] = "", ["Form.Texto"] = "", ["Form.Ordem"] = "1" }));
        var doc = await Abrir(await r.Content.ReadAsStringAsync());
        Assert.NotEmpty(doc.QuerySelectorAll("[aria-invalid='true']"));
        Assert.NotNull(doc.QuerySelector(".aviso-erro[role=alert]"));
        Assert.Empty(Verificar(doc));
        // o resumo de erros tem links para cada campo com problema
        var hrefs = doc.QuerySelectorAll(".aviso-erro a").Select(a => a.GetAttribute("href")!.TrimStart('#')).ToList();
        Assert.NotEmpty(hrefs);
        Assert.All(hrefs, h => Assert.NotNull(doc.GetElementById(h)));
    }

    [Theory]
    [InlineData("/admin/beneficios")]
    [InlineData("/admin/programas")]
    [InlineData("/admin/conteudo")]
    [InlineData("/admin/vagas/editar/1")]
    public async Task Etiqueta_exemplo_nao_aparece_no_portal_do_RH(string url)
    {
        // decisão do dono do produto: sem a etiqueta "exemplo" (a marca Ficticio continua só no banco)
        var c = await AdminAsync();
        var html = await (await c.GetAsync(url)).Content.ReadAsStringAsync();
        // só a etiqueta some; "(exemplo)" dentro do nome de um item do seed é dado e não é tocado
        Assert.DoesNotContain("etiqueta exemplo", html);
        Assert.DoesNotMatch(@">exemplo</span>", html);
    }

    [Fact]
    public async Task Cabecalho_tem_a_logo_e_a_conta_do_usuario_como_no_prototipo()
    {
        var c = await AdminAsync();
        var doc = await Abrir(await (await c.GetAsync("/admin/vagas")).Content.ReadAsStringAsync());

        // logo na marca, com texto alternativo e servida de verdade como imagem
        var logo = doc.QuerySelector(".admin-marca img.admin-logo");
        Assert.NotNull(logo);
        Assert.False(string.IsNullOrWhiteSpace(logo!.GetAttribute("alt")));
        Assert.False(string.IsNullOrEmpty(logo.GetAttribute("width")) || string.IsNullOrEmpty(logo.GetAttribute("height")));   // evita "pulo" de layout
        var resp = await c.GetAsync(logo.GetAttribute("src")!);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("image/png", resp.Content.Headers.ContentType!.MediaType);

        // conta: botão próprio (sem as classes de botão com borda), avatar, nome, perfil por extenso e seta
        var conta = doc.QuerySelector("button.conta-btn[data-acoes]");
        Assert.NotNull(conta);
        Assert.DoesNotContain("btn-neutro", conta!.ClassName);
        Assert.NotNull(conta.QuerySelector(".iniciais"));
        Assert.Equal("Administrador", conta.QuerySelector(".conta-txt small")!.TextContent);   // não "ADMINISTRADOR"
        Assert.NotNull(conta.QuerySelector("svg"));
        Assert.StartsWith("Conta de ", conta.GetAttribute("aria-label"));
        Assert.Empty(Verificar(doc));

        // a tela de entrada também leva a logo
        var entrada = await Abrir(await (await Novo().GetAsync("/admin/entrar")).Content.ReadAsStringAsync());
        Assert.NotNull(entrada.QuerySelector(".admin-marca img.admin-logo[alt]"));
    }

    [Fact]
    public void Botao_da_conta_nao_tem_borda_visivel_ate_o_mouse_passar()
    {
        var css = File.ReadAllText(Caminho("src/Carreiras.Web/wwwroot/css/carreiras.css"));
        var bloco = Regex.Match(css, @"\.conta-btn\s*\{([^}]*)\}").Groups[1].Value;
        Assert.Contains("border: 1px solid transparent", bloco);
        Assert.Matches(@"\.conta-btn:hover\s*\{[^}]*border-color", css);
        Assert.Matches(@"\.iniciais\s*\{[^}]*border-radius: 50%", css);   // avatar redondo
    }

    private static string Caminho(string relativo)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, relativo))) d = d.Parent;
        return d == null ? throw new FileNotFoundException(relativo) : Path.Combine(d.FullName, relativo);
    }

    [Fact]
    public async Task Acao_bloqueada_mostra_o_motivo_por_escrito()
    {
        await _b.DesativarTodosAsync();
        var email = $"unico-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Administrador, email: email);
        var c = Novo();
        var html = await (await c.GetAsync("/admin/entrar")).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await c.PostAsync("/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = token, ["Email"] = email, ["Senha"] = Senha }));
        var doc = await Abrir(await (await c.GetAsync("/admin/usuarios")).Content.ReadAsStringAsync());
        var bloqueado = doc.QuerySelector("button[aria-disabled='true']");
        Assert.NotNull(bloqueado);
        Assert.Contains("própria conta", doc.GetElementById(bloqueado!.GetAttribute("aria-describedby")!)!.TextContent);
        Assert.Empty(Verificar(doc));
    }
}
