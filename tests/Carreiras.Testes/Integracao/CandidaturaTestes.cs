using System.Net;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using Carreiras.Infra.Candidaturas;
using Carreiras.Testes.Acessibilidade;
using Carreiras.Testes.Dominio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

/// <summary>Critérios da Entrega 04: candidatura avulsa com confirmação por e-mail.</summary>
[Collection("banco")]
public class CandidaturaTestes : IDisposable
{
    private readonly BancoTeste _b;
    private readonly string _pastaEmails = Path.Combine(Path.GetTempPath(), "carreiras-teste-emails-" + Guid.NewGuid().ToString("N"));
    private readonly string _pastaCurriculos = Path.Combine(Path.GetTempPath(), "carreiras-teste-cv-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _fabrica;
    private readonly HttpClient _c;

    public CandidaturaTestes(BancoTeste b)
    {
        _b = b;
        Environment.SetEnvironmentVariable("ConnectionStrings__Carreiras", b.ConexaoApp);
        Environment.SetEnvironmentVariable("PathBase", null);
        _fabrica = Criar(40);
        _c = Cliente(_fabrica);
    }

    private WebApplicationFactory<Program> Criar(int tentativas) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(h =>
        {
            h.UseEnvironment("Development");
            h.UseSetting("Email:PastaSaida", _pastaEmails);
            h.UseSetting("Curriculos:Pasta", _pastaCurriculos);
            h.UseSetting("RateLimit:Tentativas", tentativas.ToString());
        });

    private static HttpClient Cliente(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public void Dispose()
    {
        _fabrica.Dispose();
        foreach (var p in new[] { _pastaEmails, _pastaCurriculos })
            try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch (IOException) { }
    }

    // ------------------------------------------------------------------ apoio

    private static string EmailNovo() => $"c{Guid.NewGuid():N}"[..12] + "@teste.local";

    private async Task<string> CriarVagaAsync(string status = "PUBLICADA")
    {
        var slug = "t-" + Guid.NewGuid().ToString("N")[..14];
        var area = "Área T-" + Guid.NewGuid().ToString("N")[..10];
        await _b.ExecutarAsync("INSERT dbo.Area (Nome) VALUES (@n)", ("@n", area));
        await _b.ExecutarAsync(@"
INSERT dbo.Vaga (Slug, Titulo, Descricao, UnidadeId, AreaId, TurnoId, TipoContratoId, ModalidadeId, Status, ExibirSalario, PublicadaEm)
VALUES (@s, @t, N'Descrição da vaga de teste.', (SELECT MIN(Id) FROM dbo.Unidade), (SELECT Id FROM dbo.Area WHERE Nome=@a),
        (SELECT MIN(Id) FROM dbo.Turno), (SELECT MIN(Id) FROM dbo.TipoContrato), (SELECT MIN(Id) FROM dbo.Modalidade), @st, 0, SYSUTCDATETIME())",
            ("@s", slug), ("@t", "Vaga Cand " + slug[2..8]), ("@a", area), ("@st", status));
        return slug;
    }

    private static async Task<IDocument> Doc(string html) =>
        await BrowsingContext.New(Configuration.Default).OpenAsync(r => r.Content(html));

    private sealed record Form(string Antiforgery, string TokenDesafio, int Soma);

    private async Task<Form> LerFormAsync(string? slug, HttpClient? c = null)
    {
        var r = await (c ?? _c).GetAsync(slug == null ? "/banco-de-talentos" : $"/candidatar?vaga={slug}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var doc = await Doc(await r.Content.ReadAsStringAsync());
        var af = doc.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        var td = doc.QuerySelector("input[name=TokenDesafio]")!.GetAttribute("value")!;
        var m = Regex.Match(doc.QuerySelector("label[for=Resposta]")!.TextContent, @"(\d+) \+ (\d+)");
        return new Form(af, td, int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value));
    }

    private sealed class Envio
    {
        public string? Slug = "";
        public string Nome = "Maria da Silva";
        public string Email = "";
        public string Telefone = "(47) 99999-1234";
        public string Cidade = "Timbó";
        public string Uf = "SC";
        public string? Pretensao = "3.500,00";
        public bool Aceite = true;
        public string ArquivoNome = "curriculo.pdf";
        public byte[]? Arquivo = CandidaturaDominioTestes.Pdf();
        public int? RespostaErrada;
        public string? Isca;
        public bool SemAntiforgery;
        public string? Rascunho;
    }

    private async Task<HttpResponseMessage> EnviarAsync(Envio e, HttpClient? c = null)
    {
        c ??= _c;
        var f = await LerFormAsync(e.Slug, c);
        var mp = new MultipartFormDataContent();
        void Campo(string n, string? v) { if (v != null) mp.Add(new StringContent(v), n); }
        if (!e.SemAntiforgery) Campo("__RequestVerificationToken", f.Antiforgery);
        if (e.Slug != null) Campo("vaga", e.Slug);
        Campo("TokenDesafio", f.TokenDesafio);
        Campo("Resposta", (e.RespostaErrada ?? f.Soma).ToString());
        Campo("Nome", e.Nome); Campo("Email", e.Email); Campo("Telefone", e.Telefone);
        Campo("Cidade", e.Cidade); Campo("Uf", e.Uf); Campo("PretensaoSalarial", e.Pretensao);
        Campo("Aceite", e.Aceite ? "true" : "false");
        Campo("site", e.Isca);
        Campo("TokenRascunho", e.Rascunho);
        if (e.Arquivo != null) mp.Add(new ByteArrayContent(e.Arquivo), "Curriculo", e.ArquivoNome);
        return await c.PostAsync(e.Slug == null ? "/banco-de-talentos" : $"/candidatar?vaga={e.Slug}", mp);
    }

    private Envio Novo(string? slug, string? email = null) => new() { Slug = slug, Email = email ?? EmailNovo() };

    private async Task<List<string>> EmailsParaAsync(string email)
    {
        await Task.Yield();
        if (!Directory.Exists(_pastaEmails)) return new();
        return Directory.GetFiles(_pastaEmails).OrderBy(x => x)
            .Select(File.ReadAllText).Where(t => t.Contains("Para: " + email)).ToList();
    }

    private static string TokenDoEmail(string corpo) => Regex.Match(corpo, @"/confirmar/([A-Za-z0-9_-]+)").Groups[1].Value;

    private async Task<string> TokenAsync(string email, int indice = -1)
    {
        var l = await EmailsParaAsync(email);
        Assert.NotEmpty(l);
        return TokenDoEmail(indice < 0 ? l[^1] : l[indice]);
    }

    /// <summary>Texto visível da resposta: o Razor codifica os acentos de valores dinâmicos como entidades HTML.</summary>
    private static async Task<string> Txt(HttpResponseMessage r) =>
        (await Doc(await r.Content.ReadAsStringAsync())).Body?.TextContent ?? "";

    private string? _slugAf;
    /// <summary>Token antifalsificação novo (qualquer formulário serve: o par cookie+token vale para a sessão do cliente).</summary>
    private async Task<string> AntiforgeryAsync()
    {
        _slugAf ??= await CriarVagaAsync();
        return (await LerFormAsync(_slugAf)).Antiforgery;
    }

    private async Task<(HttpResponseMessage Resp, string Html)> GetHtml(string url)
    {
        var r = await _c.GetAsync(url);
        return (r, await Txt(r));
    }

    private async Task<(HttpResponseMessage Resp, string Html)> ConfirmarClickAsync(string token, string? handler = null)
    {
        var url = $"/confirmar/{token}";
        var af = await AntiforgeryAsync();
        var r = await _c.PostAsync(handler == null ? url : url + "?handler=" + handler,
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("__RequestVerificationToken", af) }));
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"POST {url} => {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (r, await Txt(r));
    }

    private Task<int> Contar(string sql) => _b.Escalar<int>(sql);

    private async Task<int> CandidaturasAsync(string email) => await Contar(
        $"SELECT COUNT(*) FROM dbo.Candidatura x JOIN dbo.Candidato c ON c.Id = x.CandidatoId WHERE c.Email = N'{email}'");

    // ------------------------------------------------------------------ currículo mantido quando o envio volta com críticas

    private string PastaRascunho => Path.Combine(_pastaCurriculos, "rascunho");
    private string[] Rascunhos() => Directory.Exists(PastaRascunho) ? Directory.GetFiles(PastaRascunho) : Array.Empty<string>();

    private static async Task<string?> TokenRascunhoAsync(HttpResponseMessage r) =>
        (await Doc(await r.Content.ReadAsStringAsync())).QuerySelector("input[name=TokenRascunho]")?.GetAttribute("value");

    [Fact]
    public async Task Envio_com_critica_mantem_o_curriculo_e_o_reenvio_sem_anexar_conclui()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.RespostaErrada = 1;
        var r1 = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Contains("Arquivo mantido: curriculo.pdf", await Txt(r1));
        var token = await TokenRascunhoAsync(r1);
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Single(Rascunhos());
        Assert.Empty(await EmailsParaAsync(e.Email));

        // segunda tentativa: sem anexar de novo, só com a referência do arquivo mantido
        e.RespostaErrada = null; e.Arquivo = null; e.Rascunho = token;
        var r2 = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.Redirect, r2.StatusCode);
        Assert.NotEmpty(await EmailsParaAsync(e.Email));
        Assert.Single(Directory.GetFiles(_pastaCurriculos)); // o currículo definitivo
        Assert.Empty(Rascunhos());                          // e o rascunho foi apagado
    }

    [Fact]
    public async Task Arquivo_invalido_nao_e_mantido()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.RespostaErrada = 1; e.ArquivoNome = "falso.pdf"; e.Arquivo = System.Text.Encoding.ASCII.GetBytes("MZ isto nao e um pdf");
        var r = await EnviarAsync(e);
        var txt = await Txt(r);
        Assert.DoesNotContain("Arquivo mantido", txt);
        Assert.Null(await TokenRascunhoAsync(r));
        Assert.Empty(Rascunhos());
    }

    [Fact]
    public async Task Referencia_adulterada_ou_inexistente_nao_aceita_arquivo()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.Arquivo = null; e.Rascunho = "AAAA-referencia-falsa";
        var r = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Anexe seu currículo", await Txt(r));
        Assert.Empty(await EmailsParaAsync(e.Email));
    }

    [Fact]
    public async Task Novo_arquivo_substitui_o_mantido()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.RespostaErrada = 1;
        var r1 = await EnviarAsync(e);
        var primeiro = Rascunhos().Single();

        e.Arquivo = CandidaturaDominioTestes.Pdf(); e.ArquivoNome = "novo.pdf"; e.Rascunho = await TokenRascunhoAsync(r1);
        var r2 = await EnviarAsync(e);   // continua com a resposta errada
        Assert.Contains("Arquivo mantido: novo.pdf", await Txt(r2));
        var restante = Rascunhos();
        Assert.Single(restante);
        Assert.NotEqual(primeiro, restante[0]);
    }

    [Fact]
    public async Task Rascunho_vencido_nao_e_usado_e_e_limpo()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.RespostaErrada = 1;
        var r1 = await EnviarAsync(e);
        var token = await TokenRascunhoAsync(r1);
        File.SetCreationTimeUtc(Rascunhos().Single(), DateTime.UtcNow.AddHours(-3));

        e.RespostaErrada = null; e.Arquivo = null; e.Rascunho = token;
        var r2 = await EnviarAsync(e);
        Assert.Contains("Anexe seu currículo", await Txt(r2));
        Assert.Empty(await EmailsParaAsync(e.Email));

        // o próximo rascunho varre os vencidos
        var e2 = Novo(slug); e2.RespostaErrada = 1;
        await EnviarAsync(e2);
        Assert.Single(Rascunhos());
    }

    [Fact]
    public async Task Formulario_tem_mascara_de_telefone_lista_de_cidades_e_municipios_do_proprio_site()
    {
        var slug = await CriarVagaAsync();
        var r = await _c.GetAsync($"/candidatar?vaga={slug}");
        var doc = await Doc(await r.Content.ReadAsStringAsync());
        Assert.NotNull(doc.QuerySelector("#Telefone[placeholder][inputmode=tel]"));
        Assert.NotNull(doc.QuerySelector("#Cidade[role=combobox][aria-controls=lista-cidades]"));
        Assert.NotNull(doc.QuerySelector("ul#lista-cidades[role=listbox][hidden]"));
        var url = doc.QuerySelector("[data-municipios]")!.GetAttribute("data-municipios")!;

        var j = await _c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, j.StatusCode);
        var json = System.Text.Json.JsonDocument.Parse(await j.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(27, json.EnumerateObject().Count());
        Assert.Contains("Indaial", json.GetProperty("SC").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("Timbó", json.GetProperty("SC").EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/admin/entrar")]
    public async Task Todas_as_telas_usam_o_mesmo_icone_da_aba(string url)
    {
        var doc = await Doc(await (await _c.GetAsync(url)).Content.ReadAsStringAsync());
        // o endereço leva um carimbo de versão (logo.xxxx.png): confere que é o arquivo do logo e que ele existe
        var href = doc.QuerySelector("link[rel=icon]")?.GetAttribute("href");
        Assert.Matches(@"^/img/logo(\.[a-z0-9]+)?\.png$", href);
        Assert.Equal(HttpStatusCode.OK, (await _c.GetAsync(href)).StatusCode);
    }

    // ------------------------------------------------------------------ fluxo completo

    [Fact]
    public async Task Fluxo_completo_so_vira_candidatura_depois_do_clique_no_link()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        var r = await EnviarAsync(e);

        // enviou: vai para "falta um passo" e ainda NÃO há candidatura
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/candidatar/enviada", r.Headers.Location!.ToString());
        Assert.Equal(0, await CandidaturasAsync(e.Email));
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}' AND EmailConfirmado=1"));
        Assert.Single(await EmailsParaAsync(e.Email));

        var enviada = await Doc(await _c.GetStringAsync(r.Headers.Location.ToString()));
        Assert.Contains("Falta um passo", enviada.Body!.TextContent);
        Assert.Contains("só chega ao RH depois", enviada.Body.TextContent);
        Assert.DoesNotContain(e.Email, enviada.Body.TextContent); // e-mail mascarado

        // abrir o link (GET) não efetiva nada: mostra o botão
        var token = await TokenAsync(e.Email);
        var (rg, htmlGet) = await GetHtml($"/confirmar/{token}");
        Assert.Equal(HttpStatusCode.OK, rg.StatusCode);
        Assert.Contains("Confirmar minha candidatura", htmlGet);
        Assert.Equal(0, await CandidaturasAsync(e.Email));

        // o clique no botão (POST) efetiva
        var (rp, htmlPost) = await ConfirmarClickAsync(token);
        Assert.Equal(HttpStatusCode.OK, rp.StatusCode);
        Assert.Contains("Candidatura confirmada", htmlPost);
        Assert.Equal(1, await CandidaturasAsync(e.Email));
        Assert.Equal("RECEBIDA", await Escalar<string>($"SELECT x.Status FROM dbo.Candidatura x JOIN dbo.Candidato c ON c.Id=x.CandidatoId WHERE c.Email=N'{e.Email}'"));
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.CandidaturaStatusHist h JOIN dbo.Candidatura x ON x.Id=h.CandidaturaId JOIN dbo.Candidato c ON c.Id=x.CandidatoId WHERE c.Email=N'{e.Email}' AND h.Status='RECEBIDA'"));
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}' AND EmailConfirmado=1"));
        Assert.Equal(3500m, await Escalar<decimal>($"SELECT x.PretensaoSalarial FROM dbo.Candidatura x JOIN dbo.Candidato c ON c.Id=x.CandidatoId WHERE c.Email=N'{e.Email}'"));
        // dados pessoais pendentes saem da confirmação
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.ConfirmacaoEmail ce JOIN dbo.Candidato c ON c.Id=ce.CandidatoId WHERE c.Email=N'{e.Email}' AND (ce.Nome IS NOT NULL OR ce.Telefone IS NOT NULL OR ce.PretensaoSalarial IS NOT NULL)"));
    }

    private Task<T> Escalar<T>(string sql) => _b.Escalar<T>(sql);

    [Fact]
    public async Task Link_e_de_uso_unico_e_o_banco_guarda_so_o_hash()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        var token = await TokenAsync(e.Email);

        Assert.Contains("Candidatura confirmada", (await ConfirmarClickAsync(token)).Html);
        var (_, segunda) = await ConfirmarClickAsync(token);
        Assert.Contains("já foi usado", segunda);
        Assert.Equal(1, await CandidaturasAsync(e.Email));

        // só o hash (64 hex) está no banco; o token não aparece em nenhuma coluna
        var hash = await Escalar<string>($"SELECT TOP 1 ce.TokenHash FROM dbo.ConfirmacaoEmail ce JOIN dbo.Candidato c ON c.Id=ce.CandidatoId WHERE c.Email=N'{e.Email}'");
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.NotEqual(token, hash);
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.ConfirmacaoEmail WHERE TokenHash = N'{token}'"));
    }

    [Fact]
    public async Task Dois_cliques_ao_mesmo_tempo_geram_uma_so_candidatura()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        var token = await TokenAsync(e.Email);

        using var escopo1 = _fabrica.Services.CreateScope();
        using var escopo2 = _fabrica.Services.CreateScope();
        var t1 = escopo1.ServiceProvider.GetRequiredService<CandidaturaServico>().ConfirmarAsync(token);
        var t2 = escopo2.ServiceProvider.GetRequiredService<CandidaturaServico>().ConfirmarAsync(token);
        var res = await Task.WhenAll(t1, t2);

        Assert.Equal(1, res.Count(x => x.Status == StatusConfirmacao.Ok));
        Assert.Equal(1, await CandidaturasAsync(e.Email));
    }

    [Fact]
    public async Task Link_vencido_nao_efetiva_e_o_novo_link_funciona_e_o_antigo_e_substituido()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        var antigo = await TokenAsync(e.Email);
        await _b.ExecutarAsync("UPDATE ce SET ExpiraEm = DATEADD(HOUR,-1,SYSUTCDATETIME()), CriadaEm = DATEADD(HOUR,-25,SYSUTCDATETIME()) FROM dbo.ConfirmacaoEmail ce JOIN dbo.Candidato c ON c.Id=ce.CandidatoId WHERE c.Email=@e", ("@e", e.Email));

        var (rv, vencido) = await GetHtml($"/confirmar/{antigo}");
        Assert.Contains("Este link venceu", vencido);
        Assert.DoesNotContain("Confirmar minha candidatura", vencido);
        var (_, tentativa) = await ConfirmarClickAsync(antigo);
        Assert.Contains("Este link venceu", tentativa);
        Assert.Equal(0, await CandidaturasAsync(e.Email));

        var (_, aviso) = await ConfirmarClickAsync(antigo, "NovoLink");
        Assert.Contains("Enviamos um novo link", aviso);
        Assert.Equal(2, (await EmailsParaAsync(e.Email)).Count);

        var novo = await TokenAsync(e.Email);
        Assert.NotEqual(antigo, novo);
        Assert.Contains("Este link foi substituído", (await GetHtml($"/confirmar/{antigo}")).Html);
        Assert.Contains("Candidatura confirmada", (await ConfirmarClickAsync(novo)).Html);
        Assert.Equal(1, await CandidaturasAsync(e.Email));
        _ = rv;
    }

    [Fact]
    public async Task Novo_envio_do_formulario_invalida_o_link_anterior()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        var primeiro = await TokenAsync(e.Email);
        await EnviarAsync(e);
        var segundo = await TokenAsync(e.Email);

        Assert.NotEqual(primeiro, segundo);
        Assert.DoesNotContain("Confirmar minha candidatura", (await GetHtml($"/confirmar/{primeiro}")).Html);
        Assert.Contains("Candidatura confirmada", (await ConfirmarClickAsync(segundo)).Html);
        Assert.Equal(1, await CandidaturasAsync(e.Email));
        // o currículo do primeiro envio foi marcado para remoção (não há candidatura apontando para ele)
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Curriculo cu JOIN dbo.Candidato c ON c.Id=cu.CandidatoId WHERE c.Email=N'{e.Email}' AND cu.RemovidoEm IS NOT NULL"));
    }

    [Fact]
    public async Task Link_inexistente_e_404_e_nao_consome_nada()
    {
        var (r, html) = await GetHtml("/confirmar/token-que-nao-existe");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Contains("Link inválido", html);
    }

    // ------------------------------------------------------------------ duplicidade

    [Fact]
    public async Task Segunda_candidatura_do_mesmo_email_a_mesma_vaga_e_bloqueada_com_mensagem()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        await ConfirmarClickAsync(await TokenAsync(e.Email));

        var r = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await Txt(r);
        Assert.Contains("Você já se candidatou a esta vaga", html);
        Assert.Single(await EmailsParaAsync(e.Email)); // nenhum e-mail novo
        Assert.Equal(1, await CandidaturasAsync(e.Email));

        // e-mail com outra caixa também conta como o mesmo
        e.Email = e.Email.ToUpperInvariant();
        Assert.Contains("Você já se candidatou", await Txt(await EnviarAsync(e)));
    }

    [Fact]
    public async Task Mesmo_email_pode_se_candidatar_a_outra_vaga_e_o_candidato_e_reaproveitado()
    {
        var v1 = await CriarVagaAsync();
        var v2 = await CriarVagaAsync();
        var e = Novo(v1);
        await EnviarAsync(e);
        await ConfirmarClickAsync(await TokenAsync(e.Email));

        e.Slug = v2; e.Nome = "Nome Alterado Depois";
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(e)).StatusCode);
        // enquanto não confirma, o perfil existente não é sobrescrito
        Assert.Equal("Maria da Silva", await Escalar<string>($"SELECT Nome FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
        await ConfirmarClickAsync(await TokenAsync(e.Email));

        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
        Assert.Equal(2, await CandidaturasAsync(e.Email));
        Assert.Equal("Nome Alterado Depois", await Escalar<string>($"SELECT Nome FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
    }

    // ------------------------------------------------------------------ validações

    [Fact]
    public async Task Sem_aceite_do_termo_nao_envia_e_nada_e_gravado()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.Aceite = false;
        var r = await EnviarAsync(e);
        var html = await Txt(r);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("aceite o termo de privacidade", html);
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
        Assert.Empty(await EmailsParaAsync(e.Email));
    }

    [Fact]
    public async Task Campos_obrigatorios_vazios_mostram_erro_por_campo_e_resumo_acessivel()
    {
        var slug = await CriarVagaAsync();
        var e = new Envio { Slug = slug, Nome = "", Email = "", Telefone = "", Cidade = "", Uf = "", Arquivo = null, Aceite = false };
        var doc = await Doc(await (await EnviarAsync(e)).Content.ReadAsStringAsync());

        var resumo = doc.QuerySelector("#resumo-erros")!;
        Assert.Equal("alert", resumo.GetAttribute("role"));
        foreach (var campo in new[] { "Nome", "Email", "Telefone", "Cidade", "Uf", "Curriculo", "Aceite" })
        {
            Assert.NotNull(doc.QuerySelector($"#{campo}-erro"));
            Assert.Equal("true", doc.QuerySelector($"#{campo}")!.GetAttribute("aria-invalid"));
            Assert.NotNull(resumo.QuerySelector($"a[href='#{campo}']"));
        }
    }

    [Theory]
    [InlineData("curriculo.txt", "Formato não aceito")]
    [InlineData("curriculo.exe", "Formato não aceito")]
    public async Task Arquivo_fora_de_pdf_docx_e_rejeitado_com_mensagem_clara(string nome, string trecho)
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.ArquivoNome = nome;
        var html = await Txt(await EnviarAsync(e));
        Assert.Contains(trecho, html);
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
    }

    [Fact]
    public async Task Executavel_renomeado_para_pdf_e_rejeitado_pelo_conteudo_real()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.Arquivo = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04 };
        var html = await Txt(await EnviarAsync(e));
        Assert.Contains("não parece ser um PDF válido", html);
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Curriculo cu JOIN dbo.Candidato c ON c.Id=cu.CandidatoId WHERE c.Email=N'{e.Email}'"));
    }

    [Fact]
    public async Task Arquivo_acima_de_5_MB_e_rejeitado_com_mensagem_clara()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        var grande = new byte[(5 * 1024 * 1024) + 1024];
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(grande, 0);
        e.Arquivo = grande;
        var r = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("mais de 5 MB", await Txt(r));
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
    }

    [Fact]
    public async Task Docx_verdadeiro_e_aceito()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.ArquivoNome = "cv.docx"; e.Arquivo = CandidaturaDominioTestes.Docx();
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(e)).StatusCode);
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Curriculo cu JOIN dbo.Candidato c ON c.Id=cu.CandidatoId WHERE c.Email=N'{e.Email}' AND cu.Caminho LIKE N'%.docx'"));
    }

    [Fact]
    public async Task Pretensao_salarial_e_opcional()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.Pretensao = null;
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(e)).StatusCode);
        await ConfirmarClickAsync(await TokenAsync(e.Email));
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidatura x JOIN dbo.Candidato c ON c.Id=x.CandidatoId WHERE c.Email=N'{e.Email}' AND x.PretensaoSalarial IS NULL"));
    }

    [Fact]
    public async Task Pretensao_salarial_nunca_aparece_em_pagina_publica()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.Pretensao = "7.777,77";
        await EnviarAsync(e);
        var token = await TokenAsync(e.Email);
        var (_, enviada) = await GetHtml("/candidatar/enviada");
        var (_, confirmar) = await GetHtml($"/confirmar/{token}");
        var (_, vaga) = await GetHtml($"/vagas/{slug}");
        foreach (var h in new[] { enviada, confirmar, vaga }) Assert.DoesNotContain("7.777", h);
    }

    // ------------------------------------------------------------------ consentimento, arquivo e segurança

    [Fact]
    public async Task Consentimento_grava_versao_do_termo_data_e_ip()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        // direto no serviço, para informar um IP (o servidor de teste não tem endereço remoto)
        using (var escopo = _fabrica.Services.CreateScope())
        {
            var dados = new Carreiras.Dominio.Candidaturas.DadosCandidatura(e.Nome, e.Email, e.Telefone, e.Cidade, e.Uf, null, true);
            var r = await escopo.ServiceProvider.GetRequiredService<CandidaturaServico>().EnviarAsync(
                slug, dados, "cv.pdf", new MemoryStream(CandidaturaDominioTestes.Pdf()), "203.0.113.7", t => "http://localhost/confirmar/" + t);
            Assert.Equal(StatusEnvio.Ok, r.Status);
        }
        var cs = $"FROM dbo.ConsentimentoLgpd co JOIN dbo.Candidato c ON c.Id=co.CandidatoId WHERE c.Email=N'{e.Email}'";
        Assert.Equal(1, await Contar("SELECT COUNT(*) " + cs));
        Assert.Equal(Carreiras.Dominio.Candidaturas.TermoPrivacidade.VersaoAtual, await Escalar<string>("SELECT TOP 1 co.VersaoTermo " + cs));
        Assert.InRange(await Escalar<int>("SELECT DATEDIFF(MINUTE, MAX(co.AceitoEm), SYSUTCDATETIME()) " + cs), 0, 4);
        Assert.Equal("203.0.113.7", await Escalar<string>("SELECT TOP 1 co.Ip " + cs));
    }

    [Fact]
    public async Task Curriculo_e_guardado_renomeado_fora_do_wwwroot()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.ArquivoNome = "../../Meu CV <script>.pdf";
        await EnviarAsync(e);

        var caminho = await Escalar<string>($"SELECT cu.Caminho FROM dbo.Curriculo cu JOIN dbo.Candidato c ON c.Id=cu.CandidatoId WHERE c.Email=N'{e.Email}'");
        Assert.Matches(@"^[0-9a-f]{32}\.pdf$", caminho);
        Assert.True(File.Exists(Path.Combine(_pastaCurriculos, caminho)));
        Assert.DoesNotContain("wwwroot", Path.GetFullPath(_pastaCurriculos));
        var exibicao = await Escalar<string>($"SELECT cu.NomeArquivo FROM dbo.Curriculo cu JOIN dbo.Candidato c ON c.Id=cu.CandidatoId WHERE c.Email=N'{e.Email}'");
        Assert.DoesNotContain("..", exibicao);
        Assert.DoesNotContain("/", exibicao);

        // não há como baixar pelo site
        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync("/" + caminho)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync("/curriculos/" + caminho)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync("/storage/curriculos/" + caminho)).StatusCode);
    }

    [Fact]
    public void Armazenamento_nunca_abre_nome_fora_do_padrao()
    {
        var arm = _fabrica.Services.GetRequiredService<ArmazenamentoCurriculos>();
        Assert.Null(arm.Abrir("../../appsettings.json"));
        Assert.Null(arm.Abrir(@"..\..\x.pdf"));
        Assert.False(arm.Existe("qualquer.pdf"));
    }

    [Fact]
    public async Task Captcha_errado_bloqueia_e_nada_e_gravado()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.RespostaErrada = 999;
        var r = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("A conta não confere", await Txt(r));
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
    }

    [Fact]
    public async Task Campo_isca_preenchido_finge_sucesso_mas_nao_grava_nem_envia_email()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.Isca = "http://spam.example";
        var r = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
        Assert.Empty(await EmailsParaAsync(e.Email));
    }

    [Fact]
    public async Task Envio_sem_token_antifalsificacao_e_recusado()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.SemAntiforgery = true;
        var r = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'"));
    }

    [Fact]
    public async Task Confirmar_por_POST_sem_antifalsificacao_e_recusado_e_nao_consome()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        var token = await TokenAsync(e.Email);
        var r = await _c.PostAsync($"/confirmar/{token}", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(0, await CandidaturasAsync(e.Email));
    }

    [Fact]
    public async Task Excesso_de_tentativas_do_mesmo_ip_recebe_429()
    {
        using var f = Criar(3);
        using var c = Cliente(f);
        var slug = await CriarVagaAsync();
        HttpStatusCode? ultimo = null;
        for (var i = 0; i < 4; i++)
        {
            var e = Novo(slug); e.Aceite = false; // inválido, mas conta como tentativa
            ultimo = (await EnviarAsync(e, c)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, ultimo);
    }

    [Fact]
    public async Task Vaga_nao_publicada_ou_inexistente_nao_aceita_candidatura()
    {
        foreach (var status in new[] { "RASCUNHO", "PAUSADA", "ENCERRADA" })
        {
            var slug = await CriarVagaAsync(status);
            var r = await _c.GetAsync($"/candidatar?vaga={slug}");
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.Contains("Vaga não encontrada", await Txt(r));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync("/candidatar?vaga=nao-existe")).StatusCode);
        var semVaga = await _c.GetAsync("/candidatar");   // sem vaga: vai para o banco de talentos
        Assert.Equal(HttpStatusCode.Redirect, semVaga.StatusCode);
        Assert.EndsWith("/banco-de-talentos", semVaga.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Vaga_encerrada_entre_o_envio_e_o_clique_nao_efetiva()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        await _b.ExecutarAsync("UPDATE dbo.Vaga SET Status='ENCERRADA' WHERE Slug=@s", ("@s", slug));
        var (_, html) = await ConfirmarClickAsync(await TokenAsync(e.Email));
        Assert.Contains("não está mais aberta", html);
        Assert.Equal(0, await CandidaturasAsync(e.Email));
    }

    [Fact]
    public async Task Email_de_confirmacao_nao_traz_dados_pessoais_alem_do_necessario()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug); e.Telefone = "(47) 98888-7777"; e.Pretensao = "9.999,00";
        await EnviarAsync(e);
        var corpo = (await EmailsParaAsync(e.Email)).Single();
        Assert.Contains("/confirmar/", corpo);
        Assert.DoesNotContain("98888", corpo);
        Assert.DoesNotContain("9.999", corpo);
        Assert.DoesNotContain("Maria da Silva", corpo);
    }

    // ------------------------------------------------------------------ "falta um passo": reenvio

    [Fact]
    public async Task Pedir_novo_link_na_tela_falta_um_passo_respeita_o_intervalo_minimo()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        var r = await EnviarAsync(e);
        var url = r.Headers.Location!.ToString();
        var doc = await Doc(await _c.GetStringAsync(url));
        var af = doc.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        var refer = doc.QuerySelector("input[name=R]")!.GetAttribute("value")!;
        var corpo = () => new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("__RequestVerificationToken", af), new KeyValuePair<string, string>("R", refer) });

        // logo após o envio o pedido é recusado com orientação (limite entre envios)
        var rapido = await _c.PostAsync("/candidatar/enviada?handler=Reenviar", corpo());
        Assert.Contains("Já enviamos um link há pouco", await Txt(rapido));
        Assert.Single(await EmailsParaAsync(e.Email));

        // passado o intervalo, o novo link sai e o antigo deixa de valer
        await _b.ExecutarAsync("UPDATE ce SET CriadaEm = DATEADD(MINUTE,-5,CriadaEm) FROM dbo.ConfirmacaoEmail ce JOIN dbo.Candidato c ON c.Id=ce.CandidatoId WHERE c.Email=@e", ("@e", e.Email));
        var ok = await _c.PostAsync("/candidatar/enviada?handler=Reenviar", corpo());
        Assert.Contains("Enviamos um novo link", await Txt(ok));
        Assert.Equal(2, (await EmailsParaAsync(e.Email)).Count);
    }

    [Fact]
    public async Task Referencia_adulterada_nao_permite_reenviar()
    {
        var doc = await Doc(await _c.GetStringAsync("/candidatar/enviada?r=adulterada"));
        Assert.Null(doc.QuerySelector("input[name=R]"));
    }

    // ------------------------------------------------------------------ retenção

    [Fact]
    public async Task Limpeza_descarta_candidatura_nunca_confirmada_e_apaga_o_arquivo()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        var caminho = await Escalar<string>($"SELECT cu.Caminho FROM dbo.Curriculo cu JOIN dbo.Candidato c ON c.Id=cu.CandidatoId WHERE c.Email=N'{e.Email}'");
        Assert.True(File.Exists(Path.Combine(_pastaCurriculos, caminho)));

        // passaram-se 10 dias sem confirmar
        await _b.ExecutarAsync(@"UPDATE c SET CriadoEm = DATEADD(DAY,-10,SYSUTCDATETIME()) FROM dbo.Candidato c WHERE c.Email=@e;
UPDATE ce SET ExpiraEm = DATEADD(DAY,-9,SYSUTCDATETIME()) FROM dbo.ConfirmacaoEmail ce JOIN dbo.Candidato c ON c.Id=ce.CandidatoId WHERE c.Email=@e", ("@e", e.Email));

        using var escopo = _fabrica.Services.CreateScope();
        var (anon, apagados) = await escopo.ServiceProvider.GetRequiredService<RetencaoServico>().ExecutarAsync();

        Assert.True(anon >= 1);
        Assert.True(apagados >= 1);
        Assert.False(File.Exists(Path.Combine(_pastaCurriculos, caminho)));
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'")); // e-mail anonimizado
    }

    [Fact]
    public async Task Limpeza_nao_toca_em_candidato_com_link_ainda_valido()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        await EnviarAsync(e);
        await _b.ExecutarAsync("UPDATE dbo.Candidato SET CriadoEm = DATEADD(DAY,-10,SYSUTCDATETIME()) WHERE Email=@e", ("@e", e.Email));
        using var escopo = _fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<RetencaoServico>().ExecutarAsync();
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}' AND AnonimizadoEm IS NULL"));
    }

    // ------------------------------------------------------------------ telas: estrutura e acessibilidade

    [Fact]
    public async Task Telas_da_candidatura_passam_na_verificacao_de_estrutura()
    {
        var slug = await CriarVagaAsync();
        var e = Novo(slug);
        var envio = await EnviarAsync(e);
        var token = await TokenAsync(e.Email);

        var urls = new[] { $"/candidatar?vaga={slug}", envio.Headers.Location!.ToString(), $"/confirmar/{token}", "/termo-de-privacidade" };
        foreach (var u in urls)
        {
            var problemas = EstruturaTestes.Verificar(await Doc(await _c.GetStringAsync(u)));
            Assert.True(problemas.Count == 0, $"{u}: {string.Join(" | ", problemas)}");
        }
        var comErros = await Doc(await (await EnviarAsync(new Envio { Slug = slug, Nome = "", Email = "x", Aceite = false, Arquivo = null })).Content.ReadAsStringAsync());
        var p2 = EstruturaTestes.Verificar(comErros);
        Assert.True(p2.Count == 0, "formulário com erros: " + string.Join(" | ", p2));
    }

    [Fact]
    public async Task Formulario_tem_rotulos_associados_e_link_do_termo_em_nova_aba()
    {
        var slug = await CriarVagaAsync();
        var doc = await Doc(await _c.GetStringAsync($"/candidatar?vaga={slug}"));
        foreach (var campo in new[] { "Nome", "Email", "Telefone", "Cidade", "Uf", "Curriculo", "PretensaoSalarial", "Resposta", "Aceite" })
            Assert.NotNull(doc.QuerySelector($"label[for={campo}]"));
        var termo = doc.QuerySelector("a[href$='termo-de-privacidade']")!;
        Assert.Equal("_blank", termo.GetAttribute("target"));
        Assert.Contains("noopener", termo.GetAttribute("rel"));
        Assert.Contains("abre em outra aba", termo.TextContent);
        Assert.Equal("multipart/form-data", doc.QuerySelector("form")!.GetAttribute("enctype"));
    }

    [Fact]
    public async Task Termo_de_privacidade_mostra_a_versao_e_o_aviso_de_revisao()
    {
        var (r, html) = await GetHtml("/termo-de-privacidade");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Versão " + Carreiras.Dominio.Candidaturas.TermoPrivacidade.VersaoAtual, html);
        Assert.Contains("sem revisão jurídica", html);
        Assert.Contains("12 meses", html);
    }

    // ================================================================== Entrega 05

    private async Task<Envio> ConfirmadoAsync(string? slug)
    {
        var e = Novo(slug);
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(e)).StatusCode);
        await ConfirmarClickAsync(await TokenAsync(e.Email));
        return e;
    }

    private static string TokenTitular(string corpo) => Regex.Match(corpo, @"/meus-dados/confirmar/([A-Za-z0-9_-]+)").Groups[1].Value;

    private async Task<HttpResponseMessage> PedirAsync(string email, string? tipo, bool captchaErrado = false, HttpClient? c = null)
    {
        c ??= _c;
        var r = await c.GetAsync("/meus-dados");
        var doc = await Doc(await r.Content.ReadAsStringAsync());
        var af = doc.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        var td = doc.QuerySelector("input[name=TokenDesafio]")!.GetAttribute("value")!;
        var m = Regex.Match(doc.QuerySelector("label[for=Resposta]")!.TextContent, @"(\d+) \+ (\d+)");
        var soma = int.Parse(m.Groups[1].Value) + int.Parse(m.Groups[2].Value) + (captchaErrado ? 1 : 0);
        var campos = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", af), new("TokenDesafio", td), new("Resposta", soma.ToString()), new("Email", email),
        };
        if (tipo != null) campos.Add(new("Tipo", tipo));
        return await c.PostAsync("/meus-dados", new FormUrlEncodedContent(campos));
    }

    private async Task<HttpResponseMessage> PostTitularAsync(string token)
    {
        var af = await AntiforgeryAsync();
        return await _c.PostAsync($"/meus-dados/confirmar/{token}",
            new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("__RequestVerificationToken", af) }));
    }

    private async Task<string> UltimoTokenTitularAsync(string email)
    {
        var l = await EmailsParaAsync(email);
        return TokenTitular(l[^1]);
    }

    [Fact]
    public async Task Candidatura_espontanea_cria_candidatura_com_VagaId_nulo_so_apos_o_clique()
    {
        var e = Novo(null);
        var r = await EnviarAsync(e);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(0, await CandidaturasAsync(e.Email));
        var corpo = (await EmailsParaAsync(e.Email)).Single();
        Assert.Contains("banco de talentos", corpo);

        var token = await TokenAsync(e.Email);
        Assert.Contains("banco de talentos", (await GetHtml($"/confirmar/{token}")).Html);
        Assert.Equal(0, await CandidaturasAsync(e.Email));

        var (_, ok) = await ConfirmarClickAsync(token);
        Assert.Contains("banco de talentos", ok);
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidatura x JOIN dbo.Candidato c ON c.Id=x.CandidatoId WHERE c.Email=N'{e.Email}' AND x.VagaId IS NULL AND x.Status='RECEBIDA'"));
    }

    [Fact]
    public async Task Segunda_candidatura_espontanea_do_mesmo_email_e_bloqueada()
    {
        var e = await ConfirmadoAsync(null);
        var txt = await Txt(await EnviarAsync(e));
        Assert.Contains("banco de talentos", txt);
        Assert.Contains("Você já está", txt);
        Assert.Equal(1, await CandidaturasAsync(e.Email));
    }

    [Fact]
    public async Task Pessoa_do_banco_de_talentos_ainda_pode_se_candidatar_a_uma_vaga()
    {
        var e = await ConfirmadoAsync(null);
        e.Slug = await CriarVagaAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(e)).StatusCode);
        await ConfirmarClickAsync(await TokenAsync(e.Email));
        Assert.Equal(2, await CandidaturasAsync(e.Email));
    }

    [Fact]
    public async Task Banco_de_talentos_tem_formulario_completo_e_rodape_com_links_do_titular()
    {
        var doc = await Doc(await _c.GetStringAsync("/banco-de-talentos"));
        foreach (var campo in new[] { "Nome", "Email", "Telefone", "Cidade", "Uf", "Curriculo", "Resposta", "Aceite" })
            Assert.NotNull(doc.QuerySelector($"label[for={campo}]"));
        Assert.Null(doc.QuerySelector("input[name=vaga]"));
        Assert.NotNull(doc.QuerySelector("footer a[href$='/meus-dados']"));
        Assert.NotNull(doc.QuerySelector("footer a[href$='/termo-de-privacidade']"));
    }

    [Fact]
    public async Task Pedido_de_copia_so_e_atendido_depois_do_clique_e_registra_as_datas()
    {
        var e = await ConfirmadoAsync(await CriarVagaAsync());
        var r = await PedirAsync(e.Email, "COPIA");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Pedido recebido", await Txt(r));

        var sol = $"FROM dbo.SolicitacaoTitular s JOIN dbo.Candidato c ON c.Id=s.CandidatoId WHERE c.Email=N'{e.Email}' AND s.Tipo='COPIA'";
        Assert.Equal("ABERTA", await Escalar<string>("SELECT s.Status " + sol));
        Assert.Equal(0, await Contar("SELECT COUNT(*) " + sol + " AND s.AtendidaEm IS NOT NULL"));
        var token = await UltimoTokenTitularAsync(e.Email);
        Assert.NotEmpty(token);

        // abrir o link (GET) não atende
        Assert.Contains("Enviar a cópia", (await GetHtml($"/meus-dados/confirmar/{token}")).Html);
        Assert.Equal("ABERTA", await Escalar<string>("SELECT s.Status " + sol));
        var antes = (await EmailsParaAsync(e.Email)).Count;

        var ok = await PostTitularAsync(token);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Enviamos a cópia", await Txt(ok));
        Assert.Equal("ATENDIDA", await Escalar<string>("SELECT s.Status " + sol));
        Assert.Equal(1, await Contar("SELECT COUNT(*) " + sol + " AND s.AtendidaEm IS NOT NULL AND s.AbertaEm <= s.AtendidaEm"));

        var emails = await EmailsParaAsync(e.Email);
        Assert.Equal(antes + 1, emails.Count);
        var copia = emails.Last();
        Assert.Contains("Assunto: Cópia dos seus dados", copia);
        Assert.Contains("Maria da Silva", copia);
        Assert.Contains(e.Email, copia);
        Assert.Contains("curriculo.pdf", copia);
        Assert.Contains("RECEBIDA", copia);
        Assert.Contains("versão " + Carreiras.Dominio.Candidaturas.TermoPrivacidade.VersaoAtual, copia);
        // a cópia não altera nada
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}' AND AnonimizadoEm IS NOT NULL"));
    }

    [Fact]
    public async Task Pedido_de_exclusao_anonimiza_so_apos_o_clique_mantem_candidaturas_e_apaga_o_arquivo()
    {
        var e = await ConfirmadoAsync(await CriarVagaAsync());
        var cv = await Escalar<string>($"SELECT cu.Caminho FROM dbo.Curriculo cu JOIN dbo.Candidato c ON c.Id=cu.CandidatoId WHERE c.Email=N'{e.Email}'");
        Assert.True(File.Exists(Path.Combine(_pastaCurriculos, cv)));

        await PedirAsync(e.Email, "EXCLUSAO");
        var token = await UltimoTokenTitularAsync(e.Email);
        // antes do clique: nada mudou
        Assert.Contains("Excluir meus dados", (await GetHtml($"/meus-dados/confirmar/{token}")).Html);
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}' AND AnonimizadoEm IS NULL"));
        Assert.True(File.Exists(Path.Combine(_pastaCurriculos, cv)));

        var id = await Escalar<int>($"SELECT Id FROM dbo.Candidato WHERE Email=N'{e.Email}'");
        var ok = await PostTitularAsync(token);
        Assert.Contains("Seus dados foram excluídos", await Txt(ok));

        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}'"));       // e-mail original liberado
        Assert.Equal("Anonimizado", await Escalar<string>($"SELECT Nome FROM dbo.Candidato WHERE Id={id}"));
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Id={id} AND Telefone IS NULL AND Cidade IS NULL AND Uf IS NULL AND AnonimizadoEm IS NOT NULL"));
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidatura WHERE CandidatoId={id}"));       // candidaturas mantidas
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Curriculo WHERE CandidatoId={id} AND RemovidoEm IS NOT NULL"));
        Assert.False(File.Exists(Path.Combine(_pastaCurriculos, cv)));                                       // arquivo apagado do disco
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.ConsentimentoLgpd WHERE CandidatoId={id} AND Ip IS NOT NULL"));
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.SolicitacaoTitular WHERE CandidatoId={id} AND Tipo='EXCLUSAO' AND Status='ATENDIDA' AND AtendidaEm IS NOT NULL"));

        // link usado de novo: sem efeito
        var deNovo = await _c.GetAsync($"/meus-dados/confirmar/{token}");
        Assert.Equal(HttpStatusCode.NotFound, deNovo.StatusCode);

        // o mesmo e-mail pode abrir um cadastro novo
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(Novo(e.Slug, e.Email))).StatusCode);
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}' AND AnonimizadoEm IS NULL"));
    }

    [Fact]
    public async Task Pedido_do_titular_tem_resposta_igual_para_email_desconhecido_e_nada_e_gravado()
    {
        var existente = await ConfirmadoAsync(await CriarVagaAsync());
        var desconhecido = EmailNovo();
        var a = await Txt(await PedirAsync(existente.Email, "COPIA"));
        var b = await Txt(await PedirAsync(desconhecido, "COPIA"));
        Assert.Equal(a, b);
        Assert.Empty(await EmailsParaAsync(desconhecido));
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{desconhecido}'"));
    }

    [Fact]
    public async Task Pedido_do_titular_exige_escolha_email_valido_e_captcha()
    {
        var r1 = await Txt(await PedirAsync("sem-arroba", null));
        Assert.Contains("e-mail válido", r1);
        Assert.Contains("Escolha o que você quer pedir", r1);
        var e = await ConfirmadoAsync(await CriarVagaAsync());
        var r2 = await Txt(await PedirAsync(e.Email, "EXCLUSAO", captchaErrado: true));
        Assert.Contains("A conta não confere", r2);
        Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM dbo.SolicitacaoTitular s JOIN dbo.Candidato c ON c.Id=s.CandidatoId WHERE c.Email=N'{e.Email}'"));
    }

    [Fact]
    public async Task Link_do_titular_vencido_nao_executa_e_novo_pedido_invalida_o_anterior()
    {
        var e = await ConfirmadoAsync(await CriarVagaAsync());
        await PedirAsync(e.Email, "EXCLUSAO");
        var primeiro = await UltimoTokenTitularAsync(e.Email);
        await PedirAsync(e.Email, "EXCLUSAO");
        var segundo = await UltimoTokenTitularAsync(e.Email);
        Assert.NotEqual(primeiro, segundo);
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.SolicitacaoTitular s JOIN dbo.Candidato c ON c.Id=s.CandidatoId WHERE c.Email=N'{e.Email}'")); // pedido reaproveitado
        Assert.Contains("Este link foi substituído", (await GetHtml($"/meus-dados/confirmar/{primeiro}")).Html);

        await _b.ExecutarAsync("UPDATE ce SET ExpiraEm = DATEADD(HOUR,-1,SYSUTCDATETIME()) FROM dbo.ConfirmacaoEmail ce JOIN dbo.Candidato c ON c.Id=ce.CandidatoId WHERE c.Email=@e AND ce.UsadoEm IS NULL", ("@e", e.Email));
        Assert.Contains("Este link venceu", await Txt(await PostTitularAsync(segundo)));
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}' AND AnonimizadoEm IS NULL"));
    }

    [Fact]
    public async Task Pedidos_em_excesso_para_o_mesmo_email_sao_ignorados()
    {
        var e = await ConfirmadoAsync(await CriarVagaAsync());
        for (var i = 0; i < 5; i++) await PedirAsync(e.Email, i % 2 == 0 ? "COPIA" : "EXCLUSAO");
        Assert.Equal(3, (await EmailsParaAsync(e.Email)).Count(x => x.Contains("/meus-dados/confirmar/")));
    }

    [Fact]
    public async Task Confirmar_pedido_do_titular_exige_antifalsificacao_e_tokens_nao_se_misturam()
    {
        var e = await ConfirmadoAsync(await CriarVagaAsync());
        await PedirAsync(e.Email, "EXCLUSAO");
        var token = await UltimoTokenTitularAsync(e.Email);
        var semToken = await _c.PostAsync($"/meus-dados/confirmar/{token}", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        Assert.Equal(HttpStatusCode.BadRequest, semToken.StatusCode);
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Email=N'{e.Email}' AND AnonimizadoEm IS NULL"));

        // o token de candidatura não abre pedido do titular, e o do titular não confirma candidatura
        var outro = Novo(await CriarVagaAsync());
        await EnviarAsync(outro);
        var tokenCand = await TokenAsync(outro.Email);
        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync($"/meus-dados/confirmar/{tokenCand}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _c.GetAsync($"/confirmar/{token}")).StatusCode);
    }

    [Fact]
    public async Task Retencao_anonimiza_prazo_vencido_mantem_candidaturas_apaga_arquivo_e_libera_o_email()
    {
        var e = await ConfirmadoAsync(await CriarVagaAsync());
        var cv = await Escalar<string>($"SELECT cu.Caminho FROM dbo.Curriculo cu JOIN dbo.Candidato c ON c.Id=cu.CandidatoId WHERE c.Email=N'{e.Email}'");
        var id = await Escalar<int>($"SELECT Id FROM dbo.Candidato WHERE Email=N'{e.Email}'");
        await _b.ExecutarAsync("UPDATE dbo.Candidato SET RetencaoAte = DATEADD(DAY,-1,SYSUTCDATETIME()) WHERE Id=@i", ("@i", id));

        using var escopo = _fabrica.Services.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<RetencaoServico>().ExecutarAsync();

        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidato WHERE Id={id} AND AnonimizadoEm IS NOT NULL AND Nome=N'Anonimizado'"));
        Assert.Equal(1, await Contar($"SELECT COUNT(*) FROM dbo.Candidatura WHERE CandidatoId={id}"));
        Assert.False(File.Exists(Path.Combine(_pastaCurriculos, cv)));
        Assert.Equal(HttpStatusCode.Redirect, (await EnviarAsync(Novo(e.Slug, e.Email))).StatusCode);   // e-mail original liberado
    }

    [Fact]
    public async Task Telas_da_entrega_05_passam_na_verificacao_de_estrutura()
    {
        var e = await ConfirmadoAsync(await CriarVagaAsync());
        await PedirAsync(e.Email, "COPIA");
        var token = await UltimoTokenTitularAsync(e.Email);

        var htmls = new Dictionary<string, string>
        {
            ["/banco-de-talentos"] = await _c.GetStringAsync("/banco-de-talentos"),
            ["/meus-dados"] = await _c.GetStringAsync("/meus-dados"),
            ["/meus-dados (erros)"] = await (await PedirAsync("x", null)).Content.ReadAsStringAsync(),
            ["/meus-dados (enviado)"] = await (await PedirAsync(EmailNovo(), "COPIA")).Content.ReadAsStringAsync(),
            ["confirmar copia"] = await _c.GetStringAsync($"/meus-dados/confirmar/{token}"),
            ["link invalido"] = await (await _c.GetAsync("/meus-dados/confirmar/xxx")).Content.ReadAsStringAsync(),
        };
        foreach (var (nome, html) in htmls)
        {
            var p = EstruturaTestes.Verificar(await Doc(html));
            Assert.True(p.Count == 0, $"{nome}: {string.Join(" | ", p)}");
        }
    }
}
