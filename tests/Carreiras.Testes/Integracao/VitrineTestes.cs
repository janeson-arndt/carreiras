using System.Net;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Vagas;
using Carreiras.Testes.Acessibilidade;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

/// <summary>Critérios da Entrega 03: portal público (vitrine, lista com filtros e página da vaga).</summary>
[Collection("banco")]
public class VitrineTestes : IDisposable
{
    private readonly BancoTeste _b;
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly HttpClient _c;

    public VitrineTestes(BancoTeste b)
    {
        _b = b;
        Environment.SetEnvironmentVariable("ConnectionStrings__Carreiras", b.ConexaoApp);
        Environment.SetEnvironmentVariable("PathBase", null);
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(h => h.UseEnvironment("Development"));
        _c = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    public void Dispose() => _fabrica.Dispose();

    // ------------------------------------------------------------------ apoio

    private async Task<string> Html(string url)
    {
        var r = await _c.GetAsync(url);
        return await r.Content.ReadAsStringAsync();
    }

    private static async Task<IDocument> Doc(string html) =>
        await BrowsingContext.New(Configuration.Default).OpenAsync(r => r.Content(html));

    private async Task<IDocument> DocDe(string url) => await Doc(await Html(url));

    private Task<int> Id(string sql) => _b.Escalar<int>(sql);

    private async Task<int> NovaAreaAsync()
    {
        var nome = "Área T-" + Guid.NewGuid().ToString("N")[..10];
        await _b.ExecutarAsync("INSERT dbo.Area (Nome) VALUES (@n)", ("@n", nome));
        return await Id($"SELECT Id FROM dbo.Area WHERE Nome=N'{nome}'");
    }

    private sealed record Ids(int Timbo, int Recife, int Turno1, int Turno2, int Clt, int Estagio, int Presencial)
    {
        public static async Task<Ids> Ler(BancoTeste b) => new(
            await b.Escalar<int>("SELECT Id FROM dbo.Unidade WHERE Cidade=N'Timbó'"),
            await b.Escalar<int>("SELECT Id FROM dbo.Unidade WHERE Cidade=N'Recife'"),
            await b.Escalar<int>("SELECT MIN(Id) FROM dbo.Turno"),
            await b.Escalar<int>("SELECT MIN(Id) FROM dbo.Turno WHERE Id > (SELECT MIN(Id) FROM dbo.Turno)"),
            await b.Escalar<int>("SELECT Id FROM dbo.TipoContrato WHERE Nome=N'CLT'"),
            await b.Escalar<int>("SELECT Id FROM dbo.TipoContrato WHERE Nome=N'Estágio'"),
            await b.Escalar<int>("SELECT MIN(Id) FROM dbo.Modalidade WHERE Nome=N'Presencial'"));
    }

    private async Task<string> CriarVagaAsync(
        string titulo, string status, Ids ids, int unidade, int area, int turno, int contrato,
        decimal? min = null, decimal? max = null, bool exibir = false)
    {
        var slug = "t-" + Guid.NewGuid().ToString("N")[..14];
        await _b.ExecutarAsync(@"
INSERT dbo.Vaga (Slug, Titulo, Descricao, UnidadeId, AreaId, TurnoId, TipoContratoId, ModalidadeId, Status, SalarioMin, SalarioMax, ExibirSalario, PublicadaEm)
VALUES (@slug, @titulo, N'Descrição da vaga de teste.', @u, @a, @t, @c, @m, @status, @min, @max, @exibir,
        CASE WHEN @status IN ('PUBLICADA','PAUSADA','ENCERRADA') THEN SYSUTCDATETIME() END)",
            ("@slug", slug), ("@titulo", titulo), ("@u", unidade), ("@a", area), ("@t", turno), ("@c", contrato), ("@m", ids.Presencial),
            ("@status", status), ("@min", min), ("@max", max), ("@exibir", exibir));
        return slug;
    }

    private static string Titulo() => "Vaga T " + Guid.NewGuid().ToString("N")[..8];

    // ------------------------------------------------------------------ RN03: só PUBLICADA

    [Fact]
    public async Task Lista_e_pagina_so_mostram_vaga_publicada_e_rascunho_pausada_e_encerrada_somem()
    {
        var ids = await Ids.Ler(_b);
        var area = await NovaAreaAsync();
        var tituloPub = Titulo(); var tituloRasc = Titulo(); var tituloPaus = Titulo(); var tituloEnc = Titulo();
        var pub = await CriarVagaAsync(tituloPub, "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        var rasc = await CriarVagaAsync(tituloRasc, "RASCUNHO", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        var paus = await CriarVagaAsync(tituloPaus, "PAUSADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        var enc = await CriarVagaAsync(tituloEnc, "ENCERRADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);

        foreach (var url in new[] { $"/vagas?area={area}", $"/?area={area}" })
        {
            var html = await Html(url);
            Assert.Contains(tituloPub, html);
            Assert.DoesNotContain(tituloRasc, html);
            Assert.DoesNotContain(tituloPaus, html);
            Assert.DoesNotContain(tituloEnc, html);
        }
        // sem filtro nenhum também (o título é único)
        var tudo = await Html("/vagas");
        Assert.Contains(tituloPub, tudo);
        Assert.DoesNotContain(tituloRasc, tudo);

        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync($"/vagas/{pub}")).StatusCode);
        foreach (var slug in new[] { rasc, paus, enc })
        {
            var r = await _c.GetAsync($"/vagas/{slug}");
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);   // nem pela URL
        }
    }

    [Fact]
    public async Task Pagina_de_vaga_inexistente_e_um_404_com_mensagem_e_link_para_as_vagas()
    {
        var r = await _c.GetAsync("/vagas/nao-existe-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        var doc = await Doc(await r.Content.ReadAsStringAsync());
        Assert.Equal("Vaga não encontrada", doc.QuerySelector("h1")!.TextContent.Trim());
        Assert.Contains("não existe ou já foi encerrada", doc.QuerySelector("main")!.TextContent);
        Assert.EndsWith("/vagas", doc.QuerySelector("main a")!.GetAttribute("href"));
        Assert.Equal("noindex", doc.QuerySelector("meta[name=robots]")!.GetAttribute("content"));
        Assert.Null(doc.QuerySelector("script[type='application/ld+json']"));   // sem marcação para vaga que não existe
    }

    [Fact]
    public async Task Fluxo_pelo_servico_publicar_pausar_e_republicar_mantem_o_mesmo_endereco()
    {
        var ids = await Ids.Ler(_b);
        var ator = await _b.CriarUsuarioAsync(Carreiras.Dominio.Usuarios.Perfis.Administrador);
        _b.Ator.UsuarioRhId = ator;
        var area = await NovaAreaAsync();
        var titulo = Titulo();
        int vagaId; string slug;
        using (var s = _b.Escopo())
        {
            var svc = s.ServiceProvider.GetRequiredService<VagaServico>();
            var (r, id, _) = await svc.SalvarAsync(new VagaForm
            {
                Titulo = titulo, Descricao = "d", UnidadeId = ids.Timbo, AreaId = area, TurnoId = ids.Turno1,
                TipoContratoId = ids.Clt, ModalidadeId = ids.Presencial,
            }, ator);
            Assert.True(r.Ok);
            vagaId = id!.Value;
            slug = await _b.Escalar<string>($"SELECT Slug FROM dbo.Vaga WHERE Id={vagaId}");
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync($"/vagas/{slug}")).StatusCode);   // rascunho
        using (var s = _b.Escopo()) Assert.True((await s.ServiceProvider.GetRequiredService<VagaServico>().MudarStatusAsync(vagaId, "PUBLICADA", ator)).Ok);
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync($"/vagas/{slug}")).StatusCode);
        Assert.Contains(titulo, await Html($"/vagas?area={area}"));

        using (var s = _b.Escopo()) Assert.True((await s.ServiceProvider.GetRequiredService<VagaServico>().MudarStatusAsync(vagaId, "PAUSADA", ator)).Ok);
        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync($"/vagas/{slug}")).StatusCode);
        Assert.DoesNotContain(titulo, await Html($"/vagas?area={area}"));

        using (var s = _b.Escopo()) Assert.True((await s.ServiceProvider.GetRequiredService<VagaServico>().MudarStatusAsync(vagaId, "PUBLICADA", ator)).Ok);
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync($"/vagas/{slug}")).StatusCode);   // URL estável
    }

    // ------------------------------------------------------------------ filtros combináveis

    [Fact]
    public async Task Filtros_se_combinam_e_a_lista_mostra_a_contagem_e_o_estado_vazio()
    {
        var ids = await Ids.Ler(_b);
        var area = await NovaAreaAsync();
        var v1 = Titulo(); var v2 = Titulo(); var v3 = Titulo();
        await CriarVagaAsync(v1, "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        await CriarVagaAsync(v2, "PUBLICADA", ids, ids.Timbo, area, ids.Turno2, ids.Clt);
        await CriarVagaAsync(v3, "PUBLICADA", ids, ids.Recife, area, ids.Turno1, ids.Estagio);

        async Task<(List<string> Titulos, string Contagem)> Consulta(string query)
        {
            var d = await DocDe("/vagas?area=" + area + query);
            return (d.QuerySelectorAll("a.vaga h3").Select(h => h.TextContent.Trim()).OrderBy(x => x).ToList(),
                    d.QuerySelector("#contagem")!.TextContent.Trim());
        }

        var (todas, c3) = await Consulta("");
        Assert.Equal(new[] { v1, v2, v3 }.OrderBy(x => x), todas);
        Assert.Equal("3 vagas encontradas", c3);

        Assert.Equal(new[] { v1, v2 }.OrderBy(x => x), (await Consulta($"&unidade={ids.Timbo}")).Titulos);
        Assert.Equal(new[] { v1 }, (await Consulta($"&unidade={ids.Timbo}&turno={ids.Turno1}")).Titulos);
        Assert.Equal(new[] { v1 }, (await Consulta($"&unidade={ids.Timbo}&turno={ids.Turno1}&contrato={ids.Clt}")).Titulos);
        Assert.Equal(new[] { v3 }, (await Consulta($"&contrato={ids.Estagio}")).Titulos);
        var (um, c1) = await Consulta($"&unidade={ids.Recife}");
        Assert.Single(um);
        Assert.Equal("1 vaga encontrada", c1);

        // combinação sem resultado: mensagem acolhedora e caminho para o banco de talentos
        var vazio = await DocDe($"/vagas?area={area}&unidade={ids.Recife}&contrato={ids.Clt}");
        Assert.Empty(vazio.QuerySelectorAll("a.vaga"));
        Assert.Equal("0 vagas encontradas", vazio.QuerySelector("#contagem")!.TextContent.Trim());
        Assert.Contains("Nenhuma vaga aberta com esses filtros", vazio.QuerySelector(".vazio-lista")!.TextContent);
        Assert.EndsWith("/banco-de-talentos", vazio.QuerySelector(".vazio-lista a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Cada_botao_de_filtro_mantem_os_outros_filtros_e_desmarca_o_proprio()
    {
        var ids = await Ids.Ler(_b);
        var area = await NovaAreaAsync();
        await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        var d = await DocDe($"/vagas?unidade={ids.Timbo}&turno={ids.Turno1}&area={area}");

        var grupo = d.QuerySelectorAll("div.pills[role=group]").Single(g => g.GetAttribute("aria-labelledby") == "g-turno");
        Assert.NotNull(d.GetElementById("g-turno"));   // o grupo é nomeado pelo título "Turno"
        var form = grupo.Closest("form")!;
        var ocultos = form.QuerySelectorAll("input[type=hidden]").Select(i => i.GetAttribute("name")).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "area", "unidade" }, ocultos);   // os outros ficam; o próprio grupo não se repete

        var marcado = grupo.QuerySelectorAll("button.pill").Single(b => b.GetAttribute("aria-pressed") == "true");
        Assert.Equal("", marcado.GetAttribute("value"));       // clicar de novo desmarca
        Assert.Equal("turno", marcado.GetAttribute("name"));
        foreach (var f in d.QuerySelectorAll("div.pills").Select(g => g.Closest("form")!)) Assert.Equal("GET", f.GetAttribute("method")!.ToUpperInvariant());
        Assert.Empty(d.QuerySelectorAll("form[role]"));   // role="group" não é permitido em <form> (axe: aria-allowed-role)
        Assert.Contains("#vagas", form.GetAttribute("action"));   // volta para a lista, não para o topo
    }

    [Fact]
    public async Task Contagens_dos_filtros_e_da_faixa_de_unidades_so_consideram_vagas_publicadas()
    {
        var ids = await Ids.Ler(_b);
        var area = await NovaAreaAsync();
        await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        await CriarVagaAsync(Titulo(), "RASCUNHO", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        await CriarVagaAsync(Titulo(), "ENCERRADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);

        var esperado = await Id("SELECT COUNT(*) FROM dbo.Vaga v JOIN dbo.Unidade u ON u.Id=v.UnidadeId WHERE v.Status='PUBLICADA' AND u.Cidade=N'Timbó'");
        var d = await DocDe("/");
        var card = d.QuerySelectorAll(".und-grid a.und").Single(a => a.TextContent.Contains("Timbó"));
        Assert.Equal(esperado.ToString(), card.QuerySelector(".n")!.TextContent.Trim());
        Assert.Contains($"unidade={ids.Timbo}", card.GetAttribute("href"));
        Assert.EndsWith("#vagas", card.GetAttribute("href"));

        // a área nova só aparece no seletor com a quantidade de PUBLICADAS (1)
        var opcao = d.QuerySelectorAll("#f-area option").Single(o => o.GetAttribute("value") == area.ToString());
        Assert.NotNull(opcao);
        var pillTimbo = d.QuerySelectorAll("button.pill").First(b => b.TextContent.Contains("Timbó"));
        Assert.Matches($@"\({esperado}\)", pillTimbo.TextContent);
    }

    // ------------------------------------------------------------------ RN04: faixa salarial

    [Fact]
    public async Task Faixa_salarial_so_aparece_quando_a_vaga_manda_mostrar_e_tem_os_dois_valores()
    {
        var ids = await Ids.Ler(_b);
        var area = await NovaAreaAsync();
        var oculta = await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt, 3800, 4600, exibir: false);
        var mostra = await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt, 3800, 4600, exibir: true);
        var incompleta = await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt, 3800, null, exibir: true);

        foreach (var slug in new[] { oculta, incompleta })
        {
            var html = await Html($"/vagas/{slug}");
            Assert.DoesNotContain("Faixa salarial", html);
            Assert.DoesNotContain("3.800", html);
            Assert.DoesNotContain("baseSalary", html);   // nem na marcação para buscadores
        }
        var d = await DocDe($"/vagas/{mostra}");
        var lateral = d.QuerySelector("aside.lateral-vaga")!.TextContent;
        Assert.Contains("Faixa salarial", lateral);
        Assert.Contains("R$ 3.800 a R$ 4.600", lateral);
        using var json = JsonDocument.Parse(d.QuerySelector("script[type='application/ld+json']")!.TextContent);
        var sal = json.RootElement.GetProperty("baseSalary");
        Assert.Equal("BRL", sal.GetProperty("currency").GetString());
        Assert.Equal(3800m, sal.GetProperty("value").GetProperty("minValue").GetDecimal());
        Assert.Equal(4600m, sal.GetProperty("value").GetProperty("maxValue").GetDecimal());
        Assert.Equal("MONTH", sal.GetProperty("value").GetProperty("unitText").GetString());
    }

    // ------------------------------------------------------------------ página da vaga e JobPosting

    [Fact]
    public async Task Pagina_da_vaga_mostra_requisitos_atividades_diferenciais_beneficios_unidade_e_turno()
    {
        var ids = await Ids.Ler(_b);
        var area = await NovaAreaAsync();
        var titulo = Titulo();
        var slug = await CriarVagaAsync(titulo, "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        var vagaId = await Id($"SELECT Id FROM dbo.Vaga WHERE Slug=N'{slug}'");
        await _b.ExecutarAsync("INSERT dbo.VagaRequisito (VagaId, Texto, Obrigatorio) VALUES (@v, N'Requisito obrigatório X', 1), (@v, N'Requisito desejável Y', 0)", ("@v", vagaId));
        await _b.ExecutarAsync("INSERT dbo.VagaAtividade (VagaId, Texto) VALUES (@v, N'Atividade Z')", ("@v", vagaId));
        await _b.ExecutarAsync("INSERT dbo.VagaDiferencial (VagaId, Texto) VALUES (@v, N'Diferencial W')", ("@v", vagaId));
        await _b.ExecutarAsync("INSERT dbo.VagaBeneficio (VagaId, BeneficioId) SELECT @v, Id FROM dbo.Beneficio WHERE Nome=N'Plano de saúde'", ("@v", vagaId));

        var d = await DocDe($"/vagas/{slug}");
        Assert.Equal(titulo, d.QuerySelector("h1")!.TextContent.Trim());
        var corpo = d.QuerySelector("article.corpo-vaga")!;
        var titulos = corpo.QuerySelectorAll("h2").Select(h => h.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Sobre a vaga", "O que você vai fazer", "O que pedimos", "Seria bom ter", "Diferenciais", "Benefícios desta vaga" }, titulos);
        Assert.Contains("Requisito obrigatório X", corpo.QuerySelectorAll("h2")[2].NextElementSibling!.TextContent);
        Assert.Contains("Requisito desejável Y", corpo.QuerySelectorAll("h2")[3].NextElementSibling!.TextContent);
        Assert.Contains("Atividade Z", corpo.TextContent);
        Assert.Contains("Diferencial W", corpo.TextContent);
        Assert.Contains("Plano de saúde", corpo.QuerySelector(".chips")!.TextContent);

        var meta = d.QuerySelector(".faixa .meta")!.TextContent;
        Assert.Contains("Timbó (SC)", meta); Assert.Contains("CLT", meta); Assert.Contains("Presencial", meta);
        var lateral = d.QuerySelector("aside.lateral-vaga")!.TextContent;
        Assert.Contains("Matriz Timbó", lateral);                  // unidade
        Assert.Contains("Rua Fritz Lorenz, 2442", lateral);        // endereço
        Assert.Contains(await _b.Escalar<string>("SELECT Nome FROM dbo.Turno WHERE Id=" + ids.Turno1), lateral);   // turno
        Assert.EndsWith($"/candidatar?vaga={slug}", d.QuerySelector("aside.lateral-vaga a.btn")!.GetAttribute("href"));
        Assert.DoesNotContain("Seria bom ter", await Html($"/vagas/{await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt)}"));   // seção vazia não aparece
    }

    [Fact]
    public async Task Marcacao_JobPosting_e_valida_correta_e_a_prova_de_injecao()
    {
        var ids = await Ids.Ler(_b);
        var area = await NovaAreaAsync();
        var perigoso = "Operador </script><script>alert(1)</script> \"aspas\" & <b>negrito</b>";
        var slug = await CriarVagaAsync(perigoso, "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Estagio);

        var html = await Html($"/vagas/{slug}");
        Assert.DoesNotContain("</script><script>alert(1)", html);   // o JSON não consegue fechar a tag
        Assert.DoesNotContain("<script>alert(1)</script>", html);

        var d = await Doc(html);
        using var json = JsonDocument.Parse(d.QuerySelector("script[type='application/ld+json']")!.TextContent);
        var r = json.RootElement;
        Assert.Equal("https://schema.org", r.GetProperty("@context").GetString());
        Assert.Equal("JobPosting", r.GetProperty("@type").GetString());
        Assert.Equal(perigoso, r.GetProperty("title").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", r.GetProperty("datePosted").GetString()!);
        Assert.Equal("INTERN", r.GetProperty("employmentType").GetString());   // Estágio
        Assert.Equal("Metalúrgica Timboense S.A.", r.GetProperty("hiringOrganization").GetProperty("name").GetString());
        var end = r.GetProperty("jobLocation").GetProperty("address");
        Assert.Equal("Timbó", end.GetProperty("addressLocality").GetString());
        Assert.Equal("SC", end.GetProperty("addressRegion").GetString());
        Assert.Equal("BR", end.GetProperty("addressCountry").GetString());
        Assert.Contains("Rua Fritz Lorenz", end.GetProperty("streetAddress").GetString());
        Assert.EndsWith($"/vagas/{slug}", r.GetProperty("url").GetString());
        Assert.False(r.TryGetProperty("baseSalary", out _));
        // a descrição é HTML e o título escrito nela vem codificado
        Assert.Contains("<p>", r.GetProperty("description").GetString());

        // na tela, o título aparece como texto (codificado), nunca como marcação
        Assert.Equal(perigoso, d.QuerySelector("h1")!.TextContent.Trim());
        Assert.Empty(d.QuerySelectorAll("h1 b"));
    }

    [Theory]
    [InlineData("CLT", "FULL_TIME")]
    [InlineData("Estágio", "INTERN")]
    [InlineData("Jovem Aprendiz", "INTERN")]
    [InlineData("Temporário", "TEMPORARY")]
    [InlineData("Outro contrato", "OTHER")]
    public void Tipo_de_contrato_vira_employmentType_do_schema(string contrato, string esperado) =>
        Assert.Equal(esperado, Carreiras.Web.Vitrine.JobPostingJsonLd.TipoEmprego(contrato));

    [Fact]
    public async Task Barra_fixa_de_candidatura_so_existe_na_pagina_da_vaga()
    {
        var ids = await Ids.Ler(_b);
        var area = await NovaAreaAsync();
        var slug = await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, area, ids.Turno1, ids.Clt);
        var vaga = await DocDe($"/vagas/{slug}");
        Assert.Contains("em-vaga", vaga.Body!.ClassName);
        Assert.EndsWith($"/candidatar?vaga={slug}", vaga.QuerySelector(".barra-candidatar a.btn")!.GetAttribute("href"));
        var home = await DocDe("/");
        Assert.Null(home.QuerySelector(".barra-candidatar"));
        Assert.DoesNotContain("em-vaga", home.Body!.ClassName);
    }

    // ------------------------------------------------------------------ início: benefícios, programas, conteúdo

    [Fact]
    public async Task Beneficios_aparecem_agrupados_por_categoria_na_ordem_cadastrada_so_os_ativos_e_sem_carrossel()
    {
        var categorias = new List<string>();
        using (var s = _b.Escopo())
            categorias = await s.ServiceProvider.GetRequiredService<AppDbContext>().BeneficioCategoria
                .Where(c => c.Beneficios.Any(b => b.Ativo)).OrderBy(c => c.Ordem).ThenBy(c => c.Id).Select(c => c.Nome).ToListAsync();
        var cat1 = await Id("SELECT TOP 1 Id FROM dbo.BeneficioCategoria ORDER BY Ordem, Id");
        var ativo = "Benefício ativo " + Guid.NewGuid().ToString("N")[..6];
        var inativo = "Benefício inativo " + Guid.NewGuid().ToString("N")[..6];
        await _b.ExecutarAsync("INSERT dbo.Beneficio (CategoriaId, Nome, DescricaoCurta, Icone, Ordem, Ativo) VALUES (@c, @n, N'Frase do ativo', N'bi-star', 99, 1), (@c, @i, N'Frase do inativo', N'bi-star', 98, 0)",
            ("@c", cat1), ("@n", ativo), ("@i", inativo));

        var d = await DocDe("/");
        var secao = d.QuerySelector("#beneficios")!;
        Assert.Equal(categorias, secao.QuerySelectorAll(".cat h3").Select(h => h.TextContent.Trim()).ToList());

        Assert.Contains(ativo, secao.TextContent);
        Assert.DoesNotContain(inativo, secao.TextContent);
        // o item novo está dentro da categoria certa
        var primeiraCat = secao.QuerySelectorAll(".cat").First();
        Assert.Contains(ativo, primeiraCat.TextContent);
        var cartao = primeiraCat.QuerySelectorAll(".card-ben").Single(c => c.TextContent.Contains(ativo));
        Assert.Contains("bi-star", cartao.QuerySelector("i")!.ClassName);
        Assert.Equal("true", cartao.QuerySelector("i")!.GetAttribute("aria-hidden"));
        Assert.Contains("Frase do ativo", cartao.TextContent);

        // sem carrossel: nada de controles de slide nem rotação automática
        var html = await Html("/");
        Assert.DoesNotMatch(@"(?i)carousel|carrossel|slick|swiper|data-bs-ride", html);
        // no celular a fileira rola com o teclado (região focável e nomeada)
        foreach (var cards in secao.QuerySelectorAll(".cards"))
        {
            Assert.Equal("0", cards.GetAttribute("tabindex"));
            Assert.Equal("region", cards.GetAttribute("role"));
            Assert.False(string.IsNullOrWhiteSpace(cards.GetAttribute("aria-label")));
        }
    }

    [Fact]
    public async Task Conteudo_institucional_despublicado_some_da_vitrine_e_publicado_aparece()
    {
        var texto = "Texto institucional de teste " + Guid.NewGuid().ToString("N")[..6];
        await _b.ExecutarAsync("UPDATE dbo.ConteudoInstitucional SET Texto=@t, Publicado=1 WHERE Secao='diversidade'", ("@t", texto));
        Assert.Contains(texto, await Html("/"));

        await _b.ExecutarAsync("UPDATE dbo.ConteudoInstitucional SET Publicado=0 WHERE Secao='diversidade'");
        var oculto = await Html("/");
        Assert.DoesNotContain(texto, oculto);

        await _b.ExecutarAsync("UPDATE dbo.ConteudoInstitucional SET Publicado=1 WHERE Secao='diversidade'");
        Assert.Contains(texto, await Html("/"));
    }

    [Fact]
    public async Task Programas_aparecem_so_se_ativos()
    {
        var nome = "Programa T " + Guid.NewGuid().ToString("N")[..6];
        await _b.ExecutarAsync("INSERT dbo.Programa (Nome, Descricao, PublicoAlvo, Ativo) VALUES (@n, N'Desc', N'Público', 1)", ("@n", nome));
        Assert.Contains(nome, (await DocDe("/")).QuerySelector("#programas")!.TextContent);
        await _b.ExecutarAsync("UPDATE dbo.Programa SET Ativo=0 WHERE Nome=@n", ("@n", nome));
        Assert.DoesNotContain(nome, await Html("/"));
    }

    // ------------------------------------------------------------------ imagens, rodapé, estrutura

    [Fact]
    public async Task Toda_imagem_tem_texto_alternativo_inclusive_o_texto_que_esta_dentro_das_artes()
    {
        var ids = await Ids.Ler(_b);
        var slug = await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, await NovaAreaAsync(), ids.Turno1, ids.Clt);
        foreach (var url in new[] { "/", "/vagas", $"/vagas/{slug}", "/vagas/inexistente", "/candidatar", "/banco-de-talentos" })
        {
            var d = await DocDe(url);
            foreach (var img in d.QuerySelectorAll("img"))
                Assert.False(string.IsNullOrWhiteSpace(img.GetAttribute("alt")), $"{url}: <img src={img.GetAttribute("src")}> sem alt");
            Assert.NotEmpty(d.QuerySelectorAll("footer img"));   // o rodapé tem a logo
        }

        var home = await DocDe("/");
        var alts = home.QuerySelectorAll("img").Select(i => i.GetAttribute("alt")!).ToList();
        Assert.Contains(alts, a => a.StartsWith("Vista aérea da fábrica da Metisa"));
        // as duas artes de produto repetem, no alt, o texto que está escrito dentro da imagem
        Assert.Equal(2, alts.Count(a => a.Contains("Texto na imagem:")));
        Assert.Contains(alts, a => a.Contains("ISO 14001"));
        Assert.Contains(alts, a => a == "Metisa, Metalúrgica Timboense S.A.");
        // as imagens existem de verdade e têm tamanho declarado (sem pulo de layout)
        foreach (var img in home.QuerySelectorAll("img"))
        {
            Assert.False(string.IsNullOrEmpty(img.GetAttribute("width")) || string.IsNullOrEmpty(img.GetAttribute("height")));
            var r = await _c.GetAsync(img.GetAttribute("src")!);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.StartsWith("image/", r.Content.Headers.ContentType!.MediaType);
        }
    }

    [Fact]
    public async Task Rodape_tem_logo_unidades_e_redes_sociais_em_nova_aba_com_noopener()
    {
        var d = await DocDe("/");
        var rodape = d.QuerySelector("footer.rodape")!;
        Assert.NotNull(rodape.QuerySelector("img[alt]"));
        var redes = rodape.QuerySelectorAll(".redes a").ToList();
        Assert.Equal(3, redes.Count);
        var hosts = redes.Select(a => new Uri(a.GetAttribute("href")!).Host).ToList();
        Assert.Contains(hosts, h => h.EndsWith("facebook.com"));
        Assert.Contains(hosts, h => h.EndsWith("instagram.com"));
        Assert.Contains(hosts, h => h.EndsWith("linkedin.com"));
        foreach (var a in redes)
        {
            Assert.Equal("_blank", a.GetAttribute("target"));
            Assert.Contains("noopener", a.GetAttribute("rel"));
            Assert.Contains("noreferrer", a.GetAttribute("rel"));
            Assert.Contains("nova aba", a.GetAttribute("aria-label"));   // quem usa leitor de tela é avisado
            Assert.Equal("true", a.QuerySelector("svg")!.GetAttribute("aria-hidden"));
        }
        // as unidades vêm do cadastro: endereço e telefone
        var texto = rodape.TextContent;
        Assert.Contains("Rua Fritz Lorenz, 2442", texto);
        Assert.Contains("Recife (PE)", texto);
        Assert.Contains("(51) 3472-3454", texto);
    }

    [Fact]
    public async Task Telas_publicas_passam_na_verificacao_de_estrutura_e_acessibilidade()
    {
        var ids = await Ids.Ler(_b);
        var slug = await CriarVagaAsync(Titulo(), "PUBLICADA", ids, ids.Timbo, await NovaAreaAsync(), ids.Turno1, ids.Clt);
        foreach (var url in new[]
        {
            "/", "/vagas", $"/vagas?unidade={ids.Timbo}&turno={ids.Turno1}", "/vagas?area=999999", $"/vagas/{slug}",
            "/vagas/nao-existe", "/candidatar?vaga=" + slug, "/banco-de-talentos",
        })
        {
            var problemas = EstruturaTestes.Verificar(await DocDe(url));
            Assert.True(problemas.Count == 0, $"{url}:\n - " + string.Join("\n - ", problemas));
        }
    }

    [Fact]
    public async Task Navegacao_por_teclado_link_para_pular_conteudo_e_destinos_das_ancoras()
    {
        var d = await DocDe("/");
        var pular = d.QuerySelector("a.visually-hidden-focusable")!;
        Assert.Equal("#conteudo", pular.GetAttribute("href"));
        Assert.NotNull(d.GetElementById("conteudo"));
        // a navegação principal aponta para seções que existem
        foreach (var a in d.QuerySelectorAll("nav[aria-label=Principal] a").Where(a => a.GetAttribute("href")!.Contains('#')))
        {
            var alvo = a.GetAttribute("href")!.Split('#')[1];
            Assert.NotNull(d.GetElementById(alvo));
        }
        // botões de filtro e links são elementos nativos (focáveis), sem tabindex positivo
        Assert.Empty(d.QuerySelectorAll("[tabindex]").Where(e => int.TryParse(e.GetAttribute("tabindex"), out var t) && t > 0));
        // o seletor de área funciona sem JavaScript (botão dentro de noscript)
        Assert.NotNull(d.QuerySelector("noscript"));
    }

    [Fact]
    public async Task Aviso_de_ambiente_de_avaliacao_e_a_nota_de_uso_das_imagens_aparecem_no_ambiente_local()
    {
        var d = await DocDe("/");
        Assert.Contains("Ambiente de avaliação", d.QuerySelector(".aviso")!.TextContent);
        Assert.Contains("Imagens e logo são da Metisa", d.QuerySelector("footer .nota")!.TextContent);
    }

    [Fact]
    public async Task Cabecalhos_de_seguranca_tambem_valem_no_portal_publico()
    {
        var r = await _c.GetAsync("/");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        var csp = r.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("font-src 'self' https://fonts.gstatic.com", csp);
        // nenhum script em linha executável (o JSON-LD é dado, não script)
        var html = await r.Content.ReadAsStringAsync();
        Assert.DoesNotMatch(@"<script(?![^>]*(src=|type=""application/ld\+json""))[^>]*>", html);
    }
}
