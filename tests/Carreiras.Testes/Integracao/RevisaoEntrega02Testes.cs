using System.Net;
using System.Text.RegularExpressions;
using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Usuarios;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

/// <summary>
/// Lacunas achadas na revisão da Entrega 02 contra os critérios: o fluxo COMPLETO do convite pelo servidor real
/// (e-mail em arquivo, link de uso único, expiração), reenvio, nova senha, bloqueio por tentativas, antiforgery,
/// cabeçalhos de segurança e o comando de recuperação do administrador.
/// </summary>
[Collection("banco")]
public class RevisaoEntrega02Testes : IDisposable
{
    private const string Senha = "SenhaForte2026x";
    private readonly BancoTeste _b;
    private readonly string _pastaEmails = Path.Combine(Path.GetTempPath(), "carreiras-emails-" + Guid.NewGuid().ToString("N"));
    private readonly List<WebApplicationFactory<Program>> _fabricas = new();

    public RevisaoEntrega02Testes(BancoTeste b)
    {
        _b = b;
        Environment.SetEnvironmentVariable("ConnectionStrings__Carreiras", b.ConexaoApp);
        Environment.SetEnvironmentVariable("Email__PastaSaida", _pastaEmails);
    }

    public void Dispose()
    {
        foreach (var f in _fabricas) f.Dispose();
        try { if (Directory.Exists(_pastaEmails)) Directory.Delete(_pastaEmails, true); } catch { /* temporário */ }
    }

    private WebApplicationFactory<Program> Fabrica(Action<IServiceCollection>? servicos = null)
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(h =>
        {
            h.UseEnvironment("Development");
            if (servicos != null) h.ConfigureServices(servicos);
        });
        _fabricas.Add(f);
        return f;
    }

    private static HttpClient Cliente(WebApplicationFactory<Program> f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> Token(HttpClient c, string url)
    {
        var html = await (await c.GetAsync(url)).Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "token antiforgery não encontrado em " + url);
        return m.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> Entrar(HttpClient c, string email, string senha) =>
        await c.PostAsync("/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = await Token(c, "/admin/entrar"), ["Email"] = email, ["Senha"] = senha }));

    private async Task<HttpClient> AdminLogadoAsync(WebApplicationFactory<Program> f)
    {
        var email = $"admin-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Administrador, email: email);
        var c = Cliente(f);
        Assert.Equal(HttpStatusCode.Redirect, (await Entrar(c, email, Senha)).StatusCode);
        return c;
    }

    private static async Task<HttpResponseMessage> Post(HttpClient c, string paginaToken, string url, Dictionary<string, string>? campos = null)
    {
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = await Token(c, paginaToken) };
        if (campos != null) foreach (var kv in campos) form[kv.Key] = kv.Value;
        return await c.PostAsync(url, new FormUrlEncodedContent(form));
    }

    /// <summary>Último e-mail gravado em arquivo para o destinatário, e o link dele.</summary>
    private string? LinkDoEmail(string para)
    {
        if (!Directory.Exists(_pastaEmails)) return null;
        foreach (var arq in Directory.GetFiles(_pastaEmails).OrderByDescending(File.GetCreationTimeUtc))
        {
            var txt = File.ReadAllText(arq);
            if (txt.StartsWith($"Para: {para}\n")) return Regex.Match(txt, @"http://\S+").Value;
        }
        return null;
    }

    private int QtdEmails(string para) =>
        !Directory.Exists(_pastaEmails) ? 0 : Directory.GetFiles(_pastaEmails).Count(a => File.ReadAllText(a).StartsWith($"Para: {para}\n"));

    private async Task<int> IdPorEmail(string email) => await _b.Escalar<int>($"SELECT Id FROM dbo.UsuarioRh WHERE Email=N'{email}'");

    // ------------------------------------------------------------------ convite, ponta a ponta

    [Fact]
    public async Task Convite_completo_cadastro_link_de_uso_unico_senha_e_primeiro_acesso()
    {
        var f = Fabrica();
        var admin = await AdminLogadoAsync(f);
        var email = $"convidado-{Guid.NewGuid():N}@teste.local";

        var cad = await Post(admin, "/admin/usuarios/novo", "/admin/usuarios/novo",
            new() { ["Nome"] = "Pessoa Convidada", ["Email"] = email, ["Perfil"] = Perfis.Analista });
        Assert.Equal(HttpStatusCode.Redirect, cad.StatusCode);

        // pendente: ainda não entra
        var antes = Cliente(f);
        var tentativa = await Entrar(antes, email, Senha);
        Assert.Equal(HttpStatusCode.OK, tentativa.StatusCode);

        var link = LinkDoEmail(email);
        Assert.False(string.IsNullOrEmpty(link), "o e-mail de convite não foi gerado");
        var pessoa = Cliente(f);
        var pagina = await pessoa.GetAsync(link!);
        Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
        Assert.DoesNotContain("expirou", WebUtility.HtmlDecode(await pagina.Content.ReadAsStringAsync()));

        // senha fraca é recusada e o link continua válido
        var uid = Regex.Match(link!, @"uid=([^&]+)").Groups[1].Value;
        var tok = Regex.Match(link!, @"token=(.+)$").Groups[1].Value;
        var fraca = await Post(pessoa, link!, "/admin/definir-senha", new() { ["uid"] = uid, ["token"] = tok, ["Senha"] = "curta", ["Confirmacao"] = "curta" });
        Assert.Equal(HttpStatusCode.OK, fraca.StatusCode);
        var diferente = await Post(pessoa, link!, "/admin/definir-senha", new() { ["uid"] = uid, ["token"] = tok, ["Senha"] = Senha, ["Confirmacao"] = Senha + "x" });
        Assert.Contains("não são iguais", WebUtility.HtmlDecode(await diferente.Content.ReadAsStringAsync()));

        var ok = await Post(pessoa, link!, "/admin/definir-senha", new() { ["uid"] = uid, ["token"] = tok, ["Senha"] = Senha, ["Confirmacao"] = Senha });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

        // uso único: o mesmo link não vale mais
        var reuso = await Cliente(f).GetAsync(link!);
        Assert.Contains("expirou ou já foi usado", WebUtility.HtmlDecode(await reuso.Content.ReadAsStringAsync()));
        var reusoPost = await Post(pessoa, "/admin/entrar", "/admin/definir-senha", new() { ["uid"] = uid, ["token"] = tok, ["Senha"] = "OutraSenha2026y", ["Confirmacao"] = "OutraSenha2026y" });
        Assert.Contains("expirou ou já foi usado", WebUtility.HtmlDecode(await reusoPost.Content.ReadAsStringAsync()));

        // agora entra, com o perfil escolhido pelo administrador
        var entrada = Cliente(f);
        Assert.Equal(HttpStatusCode.Redirect, (await Entrar(entrada, email, Senha)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await entrada.GetAsync("/admin/vagas")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await entrada.GetAsync("/admin/usuarios")).StatusCode);

        // a auditoria registra o convite e a criação, nunca o link nem o token
        var tudo = await _b.Escalar<string>("SELECT COALESCE(STRING_AGG(CONCAT(Campo,'|',ValorAntigo,'|',ValorNovo), ';'),'') FROM dbo.LogAuditoria WHERE Tabela='UsuarioRh'");
        Assert.Contains("Convite enviado", tudo);
        Assert.DoesNotContain(tok, tudo);
        Assert.DoesNotContain(Senha, tudo);
    }

    [Fact]
    public async Task Link_de_convite_expira()
    {
        // prazo de 1 segundo só para este teste (em produção são 48 horas)
        var f = Fabrica(s => s.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromSeconds(1)));
        var admin = await AdminLogadoAsync(f);
        var email = $"expira-{Guid.NewGuid():N}@teste.local";
        await Post(admin, "/admin/usuarios/novo", "/admin/usuarios/novo", new() { ["Nome"] = "Expira", ["Email"] = email, ["Perfil"] = Perfis.Leitura });
        var link = LinkDoEmail(email)!;
        await Task.Delay(2500);
        var r = await Cliente(f).GetAsync(link);
        Assert.Contains("expirou ou já foi usado", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Reenviar_convite_e_enviar_nova_senha_seguem_o_mesmo_modelo_e_vao_para_a_auditoria()
    {
        var f = Fabrica();
        var admin = await AdminLogadoAsync(f);
        var email = $"reenvio-{Guid.NewGuid():N}@teste.local";
        await Post(admin, "/admin/usuarios/novo", "/admin/usuarios/novo", new() { ["Nome"] = "Reenvio", ["Email"] = email, ["Perfil"] = Perfis.Leitura });
        var id = await IdPorEmail(email);
        Assert.Equal(1, QtdEmails(email));

        // pendente: só reenvio de convite faz sentido; "nova senha" é recusada
        var recusada = await Post(admin, "/admin/usuarios", $"/admin/usuarios?handler=NovaSenha&id={id}");
        Assert.Equal(HttpStatusCode.Redirect, recusada.StatusCode);
        Assert.Equal(1, QtdEmails(email));
        Assert.Contains("ainda não aceitou", WebUtility.HtmlDecode(await (await admin.GetAsync("/admin/usuarios")).Content.ReadAsStringAsync()));

        await Post(admin, "/admin/usuarios", $"/admin/usuarios?handler=ReenviarConvite&id={id}");
        Assert.Equal(2, QtdEmails(email));

        // a pessoa aceita o convite; a partir daí o convite não pode mais ser reenviado, mas a nova senha pode
        var link = LinkDoEmail(email)!;
        var uid = Regex.Match(link, @"uid=([^&]+)").Groups[1].Value;
        var tok = Regex.Match(link, @"token=(.+)$").Groups[1].Value;
        var pessoa = Cliente(f);
        await Post(pessoa, link, "/admin/definir-senha", new() { ["uid"] = uid, ["token"] = tok, ["Senha"] = Senha, ["Confirmacao"] = Senha });

        await Post(admin, "/admin/usuarios", $"/admin/usuarios?handler=ReenviarConvite&id={id}");
        Assert.Equal(2, QtdEmails(email));   // já aceitou: nada enviado
        await Post(admin, "/admin/usuarios", $"/admin/usuarios?handler=NovaSenha&id={id}");
        Assert.Equal(3, QtdEmails(email));

        // o link de nova senha troca a senha e o link antigo deixa de valer
        var linkNova = LinkDoEmail(email)!;
        var uid2 = Regex.Match(linkNova, @"uid=([^&]+)").Groups[1].Value;
        var tok2 = Regex.Match(linkNova, @"token=(.+)$").Groups[1].Value;
        var nova = "NovaSenha2026z";
        var troca = await Post(pessoa, linkNova, "/admin/definir-senha", new() { ["uid"] = uid2, ["token"] = tok2, ["Senha"] = nova, ["Confirmacao"] = nova });
        Assert.Equal(HttpStatusCode.Redirect, troca.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Entrar(Cliente(f), email, Senha)).StatusCode);          // senha antiga não entra
        Assert.Equal(HttpStatusCode.Redirect, (await Entrar(Cliente(f), email, nova)).StatusCode);      // a nova entra

        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='UsuarioRh' AND RegistroId={id} AND Campo=N'Link de nova senha enviado'"));
        Assert.Equal(2, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='UsuarioRh' AND RegistroId={id} AND Campo=N'Convite enviado'"));
        var corpo = await _b.Escalar<string>($"SELECT COALESCE(STRING_AGG(CONCAT(Campo,ValorAntigo,ValorNovo), ';'),'') FROM dbo.LogAuditoria WHERE Tabela='UsuarioRh' AND RegistroId={id}");
        Assert.DoesNotContain(tok2, corpo);
    }

    // ------------------------------------------------------------------ segurança

    [Fact]
    public async Task Bloqueia_a_conta_depois_de_5_senhas_erradas_mesmo_com_a_senha_certa()
    {
        var f = Fabrica();
        var email = $"lock-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Analista, email: email);
        var c = Cliente(f);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await Entrar(c, email, "Errada2026x")).StatusCode);
        var r = await Entrar(c, email, Senha);   // senha certa, mas já bloqueada
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("Muitas tentativas", WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Mensagem_de_login_nao_revela_se_o_usuario_existe_ou_esta_desativado()
    {
        var f = Fabrica();
        var desativado = $"off-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Analista, ativo: false, email: desativado);
        var msgs = new List<string>();
        foreach (var (email, senha) in new[] { (desativado, Senha), ($"nao-existe-{Guid.NewGuid():N}@teste.local", Senha) })
        {
            var r = await Entrar(Cliente(f), email, senha);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            msgs.Add(Regex.Match(WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()), @"E-mail ou senha incorretos[^<]*").Value);
        }
        Assert.False(string.IsNullOrEmpty(msgs[0]));
        Assert.Equal(msgs[0], msgs[1]);
    }

    [Theory]
    [InlineData("/admin/usuarios/novo")]
    [InlineData("/admin/vagas/editar")]
    [InlineData("/admin/beneficios/editar")]
    [InlineData("/admin/programas/editar")]
    [InlineData("/admin/conteudo/editar")]
    public async Task Post_sem_token_antiforgery_e_recusado(string url)
    {
        var f = Fabrica();
        var admin = await AdminLogadoAsync(f);
        var r = await admin.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string> { ["Nome"] = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Cabecalhos_de_seguranca_e_cookie_da_sessao()
    {
        var f = Fabrica();
        var r = await Cliente(f).GetAsync("/admin/entrar");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        var csp = r.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);

        var email = $"cookie-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Leitura, email: email);
        var login = await Entrar(Cliente(f), email, Senha);
        var cookie = login.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("carreiras.rh"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, cookie);   // o cookie não carrega o e-mail em texto puro
    }

    // ------------------------------------------------------------------ recuperação

    [Fact]
    public async Task Comando_criar_admin_recupera_o_acesso_quando_nao_ha_administrador()
    {
        var f = Fabrica();
        _ = Cliente(f);   // sobe o host para ter os serviços
        await _b.DesativarTodosAsync();
        var email = $"recupera-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Leitura, ativo: false, email: email);   // existente, desativado e sem poder

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Senha"] = "Recuperada2026k" }).Build();
        var codigo = await CriarAdministrador.ExecutarAsync(f.Services, config, new[] { "criar-admin", "Recuperado", email });
        Assert.Equal(0, codigo);

        var c = Cliente(f);
        Assert.Equal(HttpStatusCode.Redirect, (await Entrar(c, email, "Recuperada2026k")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/usuarios")).StatusCode);   // voltou como administrador

        // sem senha configurada, o comando recusa e não altera nada
        var vazio = new ConfigurationBuilder().Build();
        Assert.NotEqual(0, await CriarAdministrador.ExecutarAsync(f.Services, vazio, new[] { "criar-admin", "X", "x@teste.local" }));
        // senha fraca é recusada pelo Identity
        var fraca = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Senha"] = "123" }).Build();
        Assert.NotEqual(0, await CriarAdministrador.ExecutarAsync(f.Services, fraca, new[] { "criar-admin", "X", $"fraca-{Guid.NewGuid():N}@teste.local" }));
    }

    // ------------------------------------------------------------------ explicação visível do último administrador

    [Fact]
    public async Task Unico_administrador_ve_os_dois_motivos_na_propria_linha()
    {
        await _b.DesativarTodosAsync();
        var f = Fabrica();
        var email = $"unico-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Administrador, email: email);
        var c = Cliente(f);
        await Entrar(c, email, Senha);
        var html = WebUtility.HtmlDecode(await (await c.GetAsync("/admin/usuarios")).Content.ReadAsStringAsync());
        Assert.Contains(RegrasUsuario.MotivoProprio, html);
        Assert.Contains(RegrasUsuario.MotivoUltimoAdmin, html);
    }

    [Fact]
    public void Texto_do_motivo_combina_proprio_e_ultimo_so_quando_os_dois_valem()
    {
        UsuarioItem U(int id, string perfil, bool ativo, bool aceito) => new(id, "n", "e@x", perfil, ativo, aceito, null);
        var eu = U(1, Perfis.Administrador, true, true);
        var outroAtivo = U(2, Perfis.Administrador, true, true);
        var pendente = U(3, Perfis.Administrador, true, false);
        var analista = U(4, Perfis.Analista, true, true);

        // sozinho: própria + último
        var m1 = GestaoUsuarios.MotivoBloqueioDesativar(eu, 1, new[] { eu, pendente, analista });
        Assert.Contains(RegrasUsuario.MotivoProprio, m1);
        Assert.Contains(RegrasUsuario.MotivoUltimoAdmin, m1);
        // com outro administrador que entra: só "própria conta"
        Assert.Equal(RegrasUsuario.MotivoProprio, GestaoUsuarios.MotivoBloqueioDesativar(eu, 1, new[] { eu, outroAtivo }));
        // outro alvo qualquer: liberado
        Assert.Null(GestaoUsuarios.MotivoBloqueioDesativar(outroAtivo, 1, new[] { eu, outroAtivo }));
        Assert.Null(GestaoUsuarios.MotivoBloqueioDesativar(analista, 1, new[] { eu, analista }));
    }

    [Fact]
    public async Task Vaga_nova_ja_vem_com_beneficios_marcados_matriz_timbo_e_area_de_TI_disponivel()
    {
        var c = await AdminLogadoAsync(Fabrica());
        var html = await c.GetStringAsync("/admin/vagas/editar");
        var doc = await AngleSharp.BrowsingContextExtensions.OpenAsync(
            AngleSharp.BrowsingContext.New(AngleSharp.Configuration.Default), r => r.Content(html));

        var caixas = doc.QuerySelectorAll("input[name='Form.BeneficioIds']").ToList();
        Assert.NotEmpty(caixas);
        Assert.All(caixas, x => Assert.True(x.HasAttribute("checked")));
        Assert.Equal("Matriz Timbó", doc.QuerySelector("#Form_UnidadeId option[selected]")?.TextContent.Trim());
        Assert.Contains(doc.QuerySelectorAll("#Form_AreaId option"), o => o.TextContent.Trim() == "Tecnologia da Informação");
    }
}
