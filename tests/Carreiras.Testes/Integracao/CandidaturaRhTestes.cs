using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Carreiras.Dominio.Usuarios;
using Carreiras.Testes.Acessibilidade;
using Carreiras.Testes.Dominio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carreiras.Testes.Integracao;

/// <summary>Critérios da Entrega 06: gestão de candidaturas pelo RH.</summary>
[Collection("banco")]
public class CandidaturaRhTestes : IDisposable
{
    private const string Senha = "SenhaForte2026x";
    private readonly BancoTeste _b;
    private readonly string _pastaCurriculos = Path.Combine(Path.GetTempPath(), "carreiras-teste-rh-cv-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _fabrica;

    public CandidaturaRhTestes(BancoTeste b)
    {
        _b = b;
        Environment.SetEnvironmentVariable("ConnectionStrings__Carreiras", b.ConexaoApp);
        Environment.SetEnvironmentVariable("PathBase", null);
        Directory.CreateDirectory(_pastaCurriculos);
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(h =>
        {
            h.UseEnvironment("Development");
            h.UseSetting("Curriculos:Pasta", _pastaCurriculos);
        });
    }

    public void Dispose()
    {
        _fabrica.Dispose();
        try { if (Directory.Exists(_pastaCurriculos)) Directory.Delete(_pastaCurriculos, true); } catch (IOException) { }
    }

    // ------------------------------------------------------------------ apoio

    private HttpClient Anonimo() => _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<(HttpClient C, string Nome)> LogadoAsync(string perfil)
    {
        var email = $"rh-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(perfil, email: email);
        var c = Anonimo();
        var tk = await Token(c, "/admin/entrar");
        var r = await c.PostAsync("/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tk, ["Email"] = email, ["Senha"] = Senha }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return (c, "Teste " + perfil);
    }

    private static async Task<string> Token(HttpClient c, string url)
    {
        var html = await (await c.GetAsync(url)).Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "token antiforgery não encontrado em " + url);
        return m.Groups[1].Value;
    }

    private static async Task<IDocument> Doc(string html) =>
        await BrowsingContext.New(Configuration.Default).OpenAsync(r => r.Content(html));

    private static async Task<IDocument> Pagina(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await Doc(await r.Content.ReadAsStringAsync());
    }

    private async Task<(int Id, string Slug)> NovaVagaAsync(string titulo)
    {
        var slug = "t-" + Guid.NewGuid().ToString("N")[..14];
        var area = "Área T-" + Guid.NewGuid().ToString("N")[..10];
        await _b.ExecutarAsync("INSERT dbo.Area (Nome) VALUES (@n)", ("@n", area));
        await _b.ExecutarAsync(@"
INSERT dbo.Vaga (Slug, Titulo, Descricao, UnidadeId, AreaId, TurnoId, TipoContratoId, ModalidadeId, Status, ExibirSalario, PublicadaEm)
VALUES (@s, @t, N'Descrição.', (SELECT MIN(Id) FROM dbo.Unidade), (SELECT Id FROM dbo.Area WHERE Nome=@a),
        (SELECT MIN(Id) FROM dbo.Turno), (SELECT MIN(Id) FROM dbo.TipoContrato), (SELECT MIN(Id) FROM dbo.Modalidade), 'PUBLICADA', 0, SYSUTCDATETIME())",
            ("@s", slug), ("@t", titulo), ("@a", area));
        return (await _b.Escalar<int>($"SELECT Id FROM dbo.Vaga WHERE Slug=N'{slug}'"), slug);
    }

    /// <summary>Cria candidato + currículo (arquivo real no disco) + candidatura direto no banco.</summary>
    private async Task<(int CandidaturaId, int CandidatoId, string Email, string Arquivo, byte[] Bytes)> NovaCandidaturaAsync(
        int? vagaId, string status = "RECEBIDA", string nome = "Maria Teste", decimal? pretensao = 3100m, bool anonimizado = false)
    {
        var email = $"cand-{Guid.NewGuid():N}@teste.local";
        var arquivo = Guid.NewGuid().ToString("N") + ".pdf";
        var bytes = CandidaturaDominioTestes.Pdf();
        await File.WriteAllBytesAsync(Path.Combine(_pastaCurriculos, arquivo), bytes);

        await _b.ExecutarAsync(@"
INSERT dbo.Candidato (Email, Nome, Telefone, Cidade, Uf, EmailConfirmado) VALUES (@e, @n, N'(47) 99999-0000', N'Timbó', 'SC', 1);
DECLARE @c int = SCOPE_IDENTITY();
INSERT dbo.Curriculo (CandidatoId, NomeArquivo, Caminho, TamanhoKb) VALUES (@c, N'meu curriculo.pdf', @a, 1);
DECLARE @cv int = SCOPE_IDENTITY();
INSERT dbo.Candidatura (CandidatoId, VagaId, CurriculoId, PretensaoSalarial, Status) VALUES (@c, @v, @cv, @p, @s);
INSERT dbo.CandidaturaStatusHist (CandidaturaId, Status) VALUES (SCOPE_IDENTITY(), 'RECEBIDA');",
            ("@e", email), ("@n", nome), ("@a", arquivo), ("@v", vagaId), ("@p", pretensao), ("@s", status));
        var candId = await _b.Escalar<int>($"SELECT Id FROM dbo.Candidato WHERE Email=N'{email}'");
        var id = await _b.Escalar<int>($"SELECT Id FROM dbo.Candidatura WHERE CandidatoId={candId}");
        if (anonimizado)
            await _b.ExecutarAsync("EXEC dbo.usp_AnonimizarCandidato @i", ("@i", candId));
        return (id, candId, email, arquivo, bytes);
    }

    private static string LinhaDe(IDocument d, string nome) =>
        d.QuerySelectorAll("tbody tr").FirstOrDefault(tr => tr.TextContent.Contains(nome))?.TextContent ?? "";

    // ------------------------------------------------------------------ (i) lista por vaga e filtro de status

    [Fact]
    public async Task Lista_mostra_candidaturas_por_vaga_e_filtra_por_situacao()
    {
        var (c, _) = await LogadoAsync(Perfis.Analista);
        var (v1, _) = await NovaVagaAsync("Vaga RH Um " + Guid.NewGuid().ToString("N")[..6]);
        var (v2, _) = await NovaVagaAsync("Vaga RH Dois " + Guid.NewGuid().ToString("N")[..6]);
        var a = await NovaCandidaturaAsync(v1, "RECEBIDA", "Ana Recebida Um");
        var b = await NovaCandidaturaAsync(v1, "ENTREVISTA", "Bruno Entrevista Um");
        var d = await NovaCandidaturaAsync(v2, "RECEBIDA", "Diana Outra Vaga");

        var porVaga = await Pagina(c, $"/admin/candidaturas?vaga={v1}");
        Assert.NotEqual("", LinhaDe(porVaga, "Ana Recebida Um"));
        Assert.NotEqual("", LinhaDe(porVaga, "Bruno Entrevista Um"));
        Assert.Equal("", LinhaDe(porVaga, "Diana Outra Vaga"));

        var filtrada = await Pagina(c, $"/admin/candidaturas?vaga={v1}&status=ENTREVISTA");
        Assert.Equal("", LinhaDe(filtrada, "Ana Recebida Um"));
        Assert.NotEqual("", LinhaDe(filtrada, "Bruno Entrevista Um"));

        var vazia = await Pagina(c, $"/admin/candidaturas?vaga={v2}&status=APROVADA");
        Assert.Contains("Nenhuma candidatura com esses filtros", vazia.Body!.TextContent);
        _ = (a, b, d);
    }

    [Fact]
    public async Task Pretensao_salarial_e_visivel_ao_RH_na_lista_e_no_detalhe()
    {
        var (c, _) = await LogadoAsync(Perfis.Leitura);
        var (v, _) = await NovaVagaAsync("Vaga Pretensao " + Guid.NewGuid().ToString("N")[..6]);
        var x = await NovaCandidaturaAsync(v, nome: "Paulo Pretensao", pretensao: 4321.50m);
        Assert.Contains("4.321,50", LinhaDe(await Pagina(c, $"/admin/candidaturas?vaga={v}"), "Paulo Pretensao"));
        Assert.Contains("4.321,50", (await Pagina(c, $"/admin/candidaturas/{x.CandidaturaId}")).Body!.TextContent);
    }

    // ------------------------------------------------------------------ (vi) espontâneas em visão própria

    [Fact]
    public async Task Candidaturas_espontaneas_aparecem_em_visao_separada()
    {
        var (c, _) = await LogadoAsync(Perfis.Analista);
        var (v, _) = await NovaVagaAsync("Vaga Com Cand " + Guid.NewGuid().ToString("N")[..6]);
        var comVaga = await NovaCandidaturaAsync(v, nome: "Vera ComVaga");
        var espontanea = await NovaCandidaturaAsync(null, nome: "Elias Espontaneo");

        var soEsp = await Pagina(c, "/admin/candidaturas?vaga=espontanea");
        var linha = LinhaDe(soEsp, "Elias Espontaneo");
        Assert.Contains("Candidatura espontânea", linha);
        Assert.Equal("", LinhaDe(soEsp, "Vera ComVaga"));

        var daVaga = await Pagina(c, $"/admin/candidaturas?vaga={v}");
        Assert.Equal("", LinhaDe(daVaga, "Elias Espontaneo"));

        Assert.Contains("Candidatura espontânea (banco de talentos)", (await Pagina(c, $"/admin/candidaturas/{espontanea.CandidaturaId}")).Body!.TextContent);
        _ = comVaga;
    }

    // ------------------------------------------------------------------ (ii) currículo

    [Fact]
    public async Task RH_abre_o_curriculo_como_download_com_o_tipo_certo_e_sem_cache()
    {
        var (c, _) = await LogadoAsync(Perfis.Leitura);   // consulta basta para abrir
        var x = await NovaCandidaturaAsync(null);
        var r = await c.GetAsync($"/admin/candidaturas/{x.CandidaturaId}?handler=Curriculo");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", r.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("meu curriculo.pdf", r.Content.Headers.ContentDisposition.FileName?.Trim('"') ?? r.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal(x.Bytes, await r.Content.ReadAsByteArrayAsync());
        Assert.Contains("no-store", r.Headers.CacheControl?.ToString());

        // o link aparece no detalhe
        var det = await Pagina(c, $"/admin/candidaturas/{x.CandidaturaId}");
        Assert.NotNull(det.QuerySelector("a[href*='handler=Curriculo']"));
    }

    [Fact]
    public async Task Curriculo_nao_abre_sem_login()
    {
        var x = await NovaCandidaturaAsync(null);
        var r = await Anonimo().GetAsync($"/admin/candidaturas/{x.CandidaturaId}?handler=Curriculo");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/admin/entrar", r.Headers.Location!.ToString());
        var lista = await Anonimo().GetAsync("/admin/candidaturas");
        Assert.Equal(HttpStatusCode.Redirect, lista.StatusCode);
        // e o arquivo não está em nenhum endereço público
        Assert.Equal(HttpStatusCode.NotFound, (await Anonimo().GetAsync("/" + x.Arquivo)).StatusCode);
    }

    [Fact]
    public async Task Curriculo_com_caminho_adulterado_ou_arquivo_ausente_nao_abre()
    {
        var (c, _) = await LogadoAsync(Perfis.Administrador);
        var x = await NovaCandidaturaAsync(null);
        await _b.ExecutarAsync("UPDATE cu SET Caminho = '../../appsettings.json' FROM dbo.Curriculo cu JOIN dbo.Candidatura ca ON ca.CurriculoId=cu.Id WHERE ca.Id=@i", ("@i", x.CandidaturaId));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/admin/candidaturas/{x.CandidaturaId}?handler=Curriculo")).StatusCode);

        var y = await NovaCandidaturaAsync(null);
        File.Delete(Path.Combine(_pastaCurriculos, y.Arquivo));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/admin/candidaturas/{y.CandidaturaId}?handler=Curriculo")).StatusCode);
        Assert.Contains("Arquivo indisponível", (await Pagina(c, $"/admin/candidaturas/{y.CandidaturaId}")).Body!.TextContent);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/admin/candidaturas/99999999?handler=Curriculo")).StatusCode);
    }

    // ------------------------------------------------------------------ (iv) anonimizado

    [Fact]
    public async Task Candidato_anonimizado_aparece_como_Anonimizado_sem_dados_nem_curriculo()
    {
        var (c, _) = await LogadoAsync(Perfis.Analista);
        var (v, _) = await NovaVagaAsync("Vaga Anon " + Guid.NewGuid().ToString("N")[..6]);
        var x = await NovaCandidaturaAsync(v, nome: "Nome Que Sumira", anonimizado: true);

        var lista = await Pagina(c, $"/admin/candidaturas?vaga={v}");
        Assert.Contains("Anonimizado", lista.Body!.TextContent);
        Assert.DoesNotContain("Nome Que Sumira", lista.Body.TextContent);

        var det = await Pagina(c, $"/admin/candidaturas/{x.CandidaturaId}");
        Assert.Contains("Anonimizado", det.QuerySelector("h1")!.TextContent);
        Assert.DoesNotContain(x.Email, det.Body!.TextContent);
        Assert.Contains("Arquivo removido na anonimização", det.Body.TextContent);
        Assert.Null(det.QuerySelector("a[href*='handler=Curriculo']"));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/admin/candidaturas/{x.CandidaturaId}?handler=Curriculo")).StatusCode);
    }

    // ------------------------------------------------------------------ (iii) histórico de situação

    [Fact]
    public async Task Mudar_situacao_grava_historico_com_usuario_e_data_e_atualiza_a_candidatura()
    {
        var (c, nome) = await LogadoAsync(Perfis.Analista);
        var x = await NovaCandidaturaAsync(null);
        var tk = await Token(c, $"/admin/candidaturas/{x.CandidaturaId}");

        var r = await c.PostAsync($"/admin/candidaturas/{x.CandidaturaId}?handler=Status", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tk, ["novo"] = "EM_ANALISE" }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        Assert.Equal("EM_ANALISE", await _b.Escalar<string>($"SELECT Status FROM dbo.Candidatura WHERE Id={x.CandidaturaId}"));
        Assert.Equal(1, await _b.Escalar<int>($@"SELECT COUNT(*) FROM dbo.CandidaturaStatusHist h JOIN dbo.UsuarioRh u ON u.Id = h.UsuarioRhId
            WHERE h.CandidaturaId={x.CandidaturaId} AND h.Status='EM_ANALISE' AND u.Nome=N'{nome}' AND DATEDIFF(MINUTE, h.AlteradoEm, SYSUTCDATETIME()) BETWEEN 0 AND 5"));

        // segunda mudança: histórico mantém as duas, em ordem
        var tk2 = await Token(c, $"/admin/candidaturas/{x.CandidaturaId}");
        await c.PostAsync($"/admin/candidaturas/{x.CandidaturaId}?handler=Status", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tk2, ["novo"] = "REPROVADA" }));
        var det = await Pagina(c, $"/admin/candidaturas/{x.CandidaturaId}");
        var itens = det.QuerySelectorAll(".hist-lista li").Select(li => li.TextContent).ToList();
        Assert.Equal(3, itens.Count);
        Assert.Contains("Recebida", itens[0]); Assert.Contains("candidatura enviada", itens[0]);
        Assert.Contains("Em análise", itens[1]); Assert.Contains(nome, itens[1]);
        Assert.Contains("Reprovada", itens[2]); Assert.Contains(nome, itens[2]);
    }

    [Fact]
    public async Task Mudar_para_a_mesma_situacao_ou_para_valor_invalido_e_recusado_sem_historico()
    {
        var (c, _) = await LogadoAsync(Perfis.Administrador);
        var x = await NovaCandidaturaAsync(null, "ENTREVISTA");
        foreach (var novo in new[] { "ENTREVISTA", "INEXISTENTE", "" })
        {
            var tk = await Token(c, $"/admin/candidaturas/{x.CandidaturaId}");
            var r = await c.PostAsync($"/admin/candidaturas/{x.CandidaturaId}?handler=Status", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = tk, ["novo"] = novo }));
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        }
        Assert.Equal("ENTREVISTA", await _b.Escalar<string>($"SELECT Status FROM dbo.Candidatura WHERE Id={x.CandidaturaId}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.CandidaturaStatusHist WHERE CandidaturaId={x.CandidaturaId}"));
    }

    [Fact]
    public async Task Mudar_situacao_pela_lista_tambem_grava_historico()
    {
        var (c, _) = await LogadoAsync(Perfis.Analista);
        var (v, _) = await NovaVagaAsync("Vaga Lista " + Guid.NewGuid().ToString("N")[..6]);
        var x = await NovaCandidaturaAsync(v);
        var tk = await Token(c, "/admin/candidaturas");
        var r = await c.PostAsync($"/admin/candidaturas?handler=Status&id={x.CandidaturaId}", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = tk, ["novo"] = "APROVADA", ["Vaga"] = v.ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains($"vaga={v}", r.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);   // mantém o filtro
        Assert.Equal("APROVADA", await _b.Escalar<string>($"SELECT Status FROM dbo.Candidatura WHERE Id={x.CandidaturaId}"));
        Assert.Equal(2, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.CandidaturaStatusHist WHERE CandidaturaId={x.CandidaturaId}"));
    }

    // ------------------------------------------------------------------ (v) perfil LEITURA

    [Fact]
    public async Task Perfil_LEITURA_consulta_mas_nao_consegue_mudar_situacao()
    {
        var (c, _) = await LogadoAsync(Perfis.Leitura);
        var (v, _) = await NovaVagaAsync("Vaga Leitura " + Guid.NewGuid().ToString("N")[..6]);
        var x = await NovaCandidaturaAsync(v);

        var lista = await Pagina(c, $"/admin/candidaturas?vaga={v}");
        Assert.Contains("Seu perfil só consulta", lista.Body!.TextContent);
        Assert.Empty(lista.QuerySelectorAll("tbody select"));
        var det = await Pagina(c, $"/admin/candidaturas/{x.CandidaturaId}");
        Assert.Null(det.QuerySelector("select[name=novo]"));

        // mesmo forçando o POST, o servidor recusa (acesso negado) e nada muda
        var tk = await Token(c, "/admin/candidaturas");
        foreach (var url in new[] { $"/admin/candidaturas?handler=Status&id={x.CandidaturaId}", $"/admin/candidaturas/{x.CandidaturaId}?handler=Status" })
        {
            var r = await c.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = tk, ["novo"] = "APROVADA" }));
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);   // Forbid(): o cookie leva à tela de acesso negado
            Assert.Contains("/admin/acesso-negado", r.Headers.Location!.ToString());
        }
        Assert.Equal("RECEBIDA", await _b.Escalar<string>($"SELECT Status FROM dbo.Candidatura WHERE Id={x.CandidaturaId}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.CandidaturaStatusHist WHERE CandidaturaId={x.CandidaturaId}"));
    }

    [Fact]
    public async Task Mudar_situacao_exige_token_antifalsificacao()
    {
        var (c, _) = await LogadoAsync(Perfis.Administrador);
        var x = await NovaCandidaturaAsync(null);
        var r = await c.PostAsync($"/admin/candidaturas/{x.CandidaturaId}?handler=Status", new FormUrlEncodedContent(new Dictionary<string, string> { ["novo"] = "APROVADA" }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("RECEBIDA", await _b.Escalar<string>($"SELECT Status FROM dbo.Candidatura WHERE Id={x.CandidaturaId}"));
    }

    // ------------------------------------------------------------------ navegação e telas

    [Fact]
    public async Task Menu_tem_Candidaturas_e_a_lista_de_vagas_leva_as_candidaturas_da_vaga()
    {
        var (c, _) = await LogadoAsync(Perfis.Leitura);
        var (v, _) = await NovaVagaAsync("Vaga Link " + Guid.NewGuid().ToString("N")[..6]);
        await NovaCandidaturaAsync(v);
        var vagas = await Pagina(c, "/admin/vagas");
        Assert.NotNull(vagas.QuerySelector("nav a[href$='/admin/candidaturas']"));
        Assert.NotNull(vagas.QuerySelector($"td a[href*='/admin/candidaturas?vaga={v}']"));
    }

    [Fact]
    public async Task Candidatura_inexistente_e_404()
    {
        var (c, _) = await LogadoAsync(Perfis.Administrador);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/admin/candidaturas/99999999")).StatusCode);
    }

    [Fact]
    public async Task Telas_de_candidaturas_passam_na_verificacao_de_estrutura()
    {
        var (c, _) = await LogadoAsync(Perfis.Analista);
        var (v, _) = await NovaVagaAsync("Vaga Estrutura " + Guid.NewGuid().ToString("N")[..6]);
        var x = await NovaCandidaturaAsync(v);
        var anon = await NovaCandidaturaAsync(v, anonimizado: true);
        foreach (var url in new[] { "/admin/candidaturas", $"/admin/candidaturas?vaga={v}", "/admin/candidaturas?vaga=espontanea&status=APROVADA",
                     $"/admin/candidaturas/{x.CandidaturaId}", $"/admin/candidaturas/{anon.CandidaturaId}" })
        {
            var p = EstruturaTestes.Verificar(await Pagina(c, url));
            Assert.True(p.Count == 0, $"{url}: {string.Join(" | ", p)}");
        }
    }
}
