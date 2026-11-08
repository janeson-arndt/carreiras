using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.RegularExpressions;
using Carreiras.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carreiras.Testes.Integracao;

/// <summary>Para cada ação do portal, um teste por perfil confirmando que o SERVIDOR bloqueia (não só o botão escondido).</summary>
[Collection("banco")]
public class AutorizacaoTestes : IDisposable
{
    private const string Senha = "SenhaForte2026x";
    private readonly BancoTeste _b;
    private readonly WebApplicationFactory<Program> _fabrica;

    public AutorizacaoTestes(BancoTeste b)
    {
        _b = b;
        // variável de ambiente vence o user-secrets: o app sobe contra o banco de TESTE
        Environment.SetEnvironmentVariable("ConnectionStrings__Carreiras", b.ConexaoApp);
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(h => h.UseEnvironment("Development"));
    }

    public void Dispose() => _fabrica.Dispose();

    private HttpClient NovoCliente() => _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> Token(HttpClient c, string url)
    {
        var html = await (await c.GetAsync(url)).Content.ReadAsStringAsync();
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(m.Success, "token antiforgery não encontrado em " + url);
        return m.Groups[1].Value;
    }

    private async Task<HttpClient> LogadoComoAsync(string perfil)
    {
        var email = $"{perfil.ToLower()}-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(perfil, email: email);
        var c = NovoCliente();
        var r = await c.PostAsync("/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await Token(c, "/admin/entrar"),
            ["Email"] = email, ["Senha"] = Senha,
        }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.DoesNotContain("entrar", r.Headers.Location!.ToString());
        return c;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient c, string paginaComToken, string urlPost, Dictionary<string, string>? campos = null)
    {
        var form = new Dictionary<string, string> { ["__RequestVerificationToken"] = await Token(c, paginaComToken) };
        if (campos != null) foreach (var kv in campos) form[kv.Key] = kv.Value;
        return await c.PostAsync(urlPost, new FormUrlEncodedContent(form));
    }

    private static bool Negado(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.Forbidden ||
        (r.StatusCode == HttpStatusCode.Redirect && r.Headers.Location!.ToString().Contains("acesso-negado"));

    private async Task<int> NovaVagaRascunhoAsync()
    {
        await BancoTeste.Executar(_b.ConexaoAdmin, @"
INSERT dbo.Vaga (Slug, Titulo, Descricao, UnidadeId, AreaId, TurnoId, TipoContratoId, ModalidadeId, Status)
SELECT TOP 1 'auth-' + LOWER(REPLACE(CONVERT(varchar(36), NEWID()),'-','')), N'Vaga de autorização', N'x', u.Id, a.Id, t.Id, c.Id, m.Id, 'RASCUNHO'
FROM dbo.Unidade u, dbo.Area a, dbo.Turno t, dbo.TipoContrato c, dbo.Modalidade m");
        return await _b.Escalar<int>("SELECT MAX(Id) FROM dbo.Vaga WHERE Titulo = N'Vaga de autorização'");
    }

    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/vagas")]
    [InlineData("/admin/vagas/editar")]
    [InlineData("/admin/usuarios")]
    [InlineData("/admin/usuarios/novo")]
    public async Task Anonimo_e_enviado_para_a_entrada(string url)
    {
        var r = await NovoCliente().GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/admin/entrar", r.Headers.Location!.ToString());
    }

    [Theory]
    [InlineData(Perfis.Administrador)]
    [InlineData(Perfis.Analista)]
    [InlineData(Perfis.Leitura)]
    public async Task Todos_os_perfis_consultam_as_vagas(string perfil)
    {
        var c = await LogadoComoAsync(perfil);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/vagas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/vagas/editar")).StatusCode);
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, false)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Usuarios_sao_so_do_administrador(string perfil, bool permitido)
    {
        var c = await LogadoComoAsync(perfil);
        foreach (var url in new[] { "/admin/usuarios", "/admin/usuarios/novo", "/admin/usuarios/1" })
        {
            var r = await c.GetAsync(url);
            if (permitido) Assert.NotEqual(HttpStatusCode.Redirect, r.StatusCode);
            else Assert.True(Negado(r), $"{perfil} acessou {url}: {r.StatusCode}");
        }
    }

    [Theory]
    [InlineData(Perfis.Analista)]
    [InlineData(Perfis.Leitura)]
    public async Task Quem_nao_e_administrador_nao_cadastra_nem_desativa_usuarios(string perfil)
    {
        var alvo = await _b.CriarUsuarioAsync(Perfis.Leitura);
        var c = await LogadoComoAsync(perfil);

        // o token vem de uma página que o perfil ALCANÇA (vagas), então o bloqueio testado é o de autorização
        var cadastro = await PostAsync(c, "/admin/vagas", "/admin/usuarios/novo",
            new() { ["Nome"] = "X", ["Email"] = $"x-{Guid.NewGuid():N}@teste.local", ["Perfil"] = Perfis.Administrador });
        Assert.True(Negado(cadastro));

        var desativa = await PostAsync(c, "/admin/vagas", $"/admin/usuarios?handler=Desativar&id={alvo}");
        Assert.True(Negado(desativa));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT CAST(Ativo AS int) FROM dbo.UsuarioRh WHERE Id={alvo}"));
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, true)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Mudar_status_de_vaga_exige_perfil_que_gerencia(string perfil, bool permitido)
    {
        var vaga = await NovaVagaRascunhoAsync();
        var c = await LogadoComoAsync(perfil);
        var r = await PostAsync(c, "/admin/vagas", $"/admin/vagas?handler=Status&id={vaga}&novo=PUBLICADA");
        var status = await _b.Escalar<string>($"SELECT Status FROM dbo.Vaga WHERE Id={vaga}");
        if (permitido) Assert.Equal("PUBLICADA", status);
        else { Assert.True(Negado(r)); Assert.Equal("RASCUNHO", status); }
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, true)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Duplicar_vaga_exige_perfil_que_gerencia(string perfil, bool permitido)
    {
        var vaga = await NovaVagaRascunhoAsync();
        var antes = await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.Vaga");
        var c = await LogadoComoAsync(perfil);
        var r = await PostAsync(c, "/admin/vagas", $"/admin/vagas?handler=Duplicar&id={vaga}");
        var depois = await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.Vaga");
        if (permitido) Assert.Equal(antes + 1, depois);
        else { Assert.True(Negado(r)); Assert.Equal(antes, depois); }
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, true)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Editar_vaga_exige_perfil_que_gerencia(string perfil, bool permitido)
    {
        var vaga = await NovaVagaRascunhoAsync();
        var c = await LogadoComoAsync(perfil);
        var campos = new Dictionary<string, string>
        {
            ["Form.Titulo"] = "Título editado pelo teste", ["Form.Descricao"] = "d",
            ["Form.UnidadeId"] = (await _b.Escalar<int>("SELECT MIN(Id) FROM dbo.Unidade")).ToString(),
            ["Form.AreaId"] = (await _b.Escalar<int>("SELECT MIN(Id) FROM dbo.Area")).ToString(),
            ["Form.TurnoId"] = (await _b.Escalar<int>("SELECT MIN(Id) FROM dbo.Turno")).ToString(),
            ["Form.TipoContratoId"] = (await _b.Escalar<int>("SELECT MIN(Id) FROM dbo.TipoContrato")).ToString(),
            ["Form.ModalidadeId"] = (await _b.Escalar<int>("SELECT MIN(Id) FROM dbo.Modalidade")).ToString(),
            ["Form.ExibirSalario"] = "false",
        };
        var r = await PostAsync(c, $"/admin/vagas/editar/{vaga}", $"/admin/vagas/editar/{vaga}", campos);
        var titulo = await _b.Escalar<string>($"SELECT Titulo FROM dbo.Vaga WHERE Id={vaga}");
        if (permitido) Assert.Equal("Título editado pelo teste", titulo);
        else { Assert.True(Negado(r)); Assert.Equal("Vaga de autorização", titulo); }
    }

    // RN13: Benefícios e Programas são do administrador; ANALISTA e LEITURA só consultam.
    [Theory]
    [InlineData(Perfis.Administrador)]
    [InlineData(Perfis.Analista)]
    [InlineData(Perfis.Leitura)]
    public async Task Todos_os_perfis_consultam_beneficios_e_programas(string perfil)
    {
        var c = await LogadoComoAsync(perfil);
        foreach (var url in new[] { "/admin/beneficios", "/admin/programas", "/admin/beneficios/editar/1", "/admin/programas/editar/1" })
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, false)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Editar_beneficio_e_so_do_administrador(string perfil, bool permitido)
    {
        var id = await _b.Escalar<int>("SELECT MIN(Id) FROM dbo.Beneficio WHERE Nome=N'Vale-transporte'");
        var cat = await _b.Escalar<int>($"SELECT CategoriaId FROM dbo.Beneficio WHERE Id={id}");
        var desc = "descricao-" + perfil;
        var c = await LogadoComoAsync(perfil);
        var r = await PostAsync(c, $"/admin/beneficios/editar/{id}", $"/admin/beneficios/editar/{id}", new()
        {
            ["Form.Nome"] = "Vale-transporte", ["Form.CategoriaId"] = cat.ToString(), ["Form.DescricaoCurta"] = desc,
            ["Form.Icone"] = "bi-bus-front", ["Form.Ordem"] = "2",
        });
        var atual = await _b.Escalar<string>($"SELECT COALESCE(DescricaoCurta,'') FROM dbo.Beneficio WHERE Id={id}");
        if (permitido) Assert.Equal(desc, atual);
        else { Assert.True(Negado(r)); Assert.NotEqual(desc, atual); }
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, false)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Desativar_beneficio_e_so_do_administrador(string perfil, bool permitido)
    {
        var nome = "Alvo " + Guid.NewGuid().ToString("N")[..8];
        var cat = await _b.Escalar<int>("SELECT MIN(Id) FROM dbo.BeneficioCategoria");
        await BancoTeste.Executar(_b.ConexaoAdmin, $"INSERT dbo.Beneficio (CategoriaId, Nome) VALUES ({cat}, N'{nome}')");
        var id = await _b.Escalar<int>($"SELECT Id FROM dbo.Beneficio WHERE Nome=N'{nome}'");
        var c = await LogadoComoAsync(perfil);
        var r = await PostAsync(c, "/admin/beneficios", $"/admin/beneficios?handler=Alternar&id={id}&ativo=false");
        var ativo = await _b.Escalar<int>($"SELECT CAST(Ativo AS int) FROM dbo.Beneficio WHERE Id={id}");
        if (permitido) Assert.Equal(0, ativo);
        else { Assert.True(Negado(r)); Assert.Equal(1, ativo); }
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, false)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Criar_programa_e_so_do_administrador(string perfil, bool permitido)
    {
        var nome = "Programa auth " + perfil + " " + Guid.NewGuid().ToString("N")[..6];
        var c = await LogadoComoAsync(perfil);
        var r = await PostAsync(c, "/admin/programas", "/admin/programas/editar", new() { ["Form.Nome"] = nome });
        var existe = await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.Programa WHERE Nome=N'{nome}'");
        if (permitido) Assert.Equal(1, existe);
        else { Assert.True(Negado(r)); Assert.Equal(0, existe); }
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, false)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Desativar_programa_e_so_do_administrador(string perfil, bool permitido)
    {
        var nome = "Prog alvo " + Guid.NewGuid().ToString("N")[..8];
        await BancoTeste.Executar(_b.ConexaoAdmin, $"INSERT dbo.Programa (Nome) VALUES (N'{nome}')");
        var id = await _b.Escalar<int>($"SELECT Id FROM dbo.Programa WHERE Nome=N'{nome}'");
        var c = await LogadoComoAsync(perfil);
        var r = await PostAsync(c, "/admin/programas", $"/admin/programas?handler=Alternar&id={id}&ativo=false");
        var ativo = await _b.Escalar<int>($"SELECT CAST(Ativo AS int) FROM dbo.Programa WHERE Id={id}");
        if (permitido) Assert.Equal(0, ativo);
        else { Assert.True(Negado(r)); Assert.Equal(1, ativo); }
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, false)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Auditoria_e_so_do_administrador(string perfil, bool permitido)
    {
        var c = await LogadoComoAsync(perfil);
        foreach (var url in new[] { "/admin/auditoria", "/admin/auditoria?Tabela=Vaga&Pagina=2&De=2020-01-01", "/admin/auditoria?De=2026-12-31&Ate=2026-01-01" })
        {
            var r = await c.GetAsync(url);
            if (permitido) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            else Assert.True(Negado(r), $"{perfil} acessou {url}: {r.StatusCode}");
        }
    }

    [Fact]
    public async Task Pagina_de_auditoria_mostra_aviso_quando_o_periodo_e_invalido()
    {
        var c = await LogadoComoAsync(Perfis.Administrador);
        var html = WebUtility.HtmlDecode(await (await c.GetAsync("/admin/auditoria?De=2026-12-31&Ate=2026-01-01")).Content.ReadAsStringAsync());
        Assert.Contains("data final não pode ser anterior", html);
    }

    [Theory]
    [InlineData(Perfis.Administrador)]
    [InlineData(Perfis.Analista)]
    [InlineData(Perfis.Leitura)]
    public async Task Todos_os_perfis_consultam_o_conteudo(string perfil)
    {
        var id = await _b.Escalar<int>("SELECT Id FROM dbo.ConteudoInstitucional WHERE Secao='sobre'");
        var c = await LogadoComoAsync(perfil);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/conteudo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/admin/conteudo/editar/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, false)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Criar_conteudo_e_so_do_administrador(string perfil, bool permitido)
    {
        var secao = "auth-" + perfil.ToLower() + "-" + Guid.NewGuid().ToString("N")[..6];
        var c = await LogadoComoAsync(perfil);
        var r = await PostAsync(c, "/admin/conteudo", "/admin/conteudo/editar", new()
        { ["Form.Secao"] = secao, ["Form.Titulo"] = "Título", ["Form.Texto"] = "Texto", ["Form.Ordem"] = "1", ["Form.Publicado"] = "true" });
        var existe = await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.ConteudoInstitucional WHERE Secao=N'{secao}'");
        if (permitido) Assert.Equal(1, existe);
        else { Assert.True(Negado(r)); Assert.Equal(0, existe); }
    }

    [Theory]
    [InlineData(Perfis.Administrador, true)]
    [InlineData(Perfis.Analista, false)]
    [InlineData(Perfis.Leitura, false)]
    public async Task Despublicar_conteudo_e_so_do_administrador(string perfil, bool permitido)
    {
        var secao = "alvo-" + Guid.NewGuid().ToString("N")[..8];
        await BancoTeste.Executar(_b.ConexaoAdmin, $"INSERT dbo.ConteudoInstitucional (Secao, Titulo, Texto) VALUES (N'{secao}', N't', N'x')");
        var id = await _b.Escalar<int>($"SELECT Id FROM dbo.ConteudoInstitucional WHERE Secao=N'{secao}'");
        var c = await LogadoComoAsync(perfil);
        var r = await PostAsync(c, "/admin/conteudo", $"/admin/conteudo?handler=Publicar&id={id}&publicado=false");
        var publicado = await _b.Escalar<int>($"SELECT CAST(Publicado AS int) FROM dbo.ConteudoInstitucional WHERE Id={id}");
        if (permitido) Assert.Equal(0, publicado);
        else { Assert.True(Negado(r)); Assert.Equal(1, publicado); }
    }

    [Fact]
    public async Task Usuario_desativado_perde_a_sessao_aberta_e_nao_volta_a_entrar()
    {
        await _b.DesativarTodosAsync();
        var email = $"sessao-{Guid.NewGuid():N}@teste.local";
        var id = await _b.CriarUsuarioAsync(Perfis.Analista, email: email);
        var admin = await _b.CriarUsuarioAsync(Perfis.Administrador);

        var c = NovoCliente();
        await c.PostAsync("/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = await Token(c, "/admin/entrar"), ["Email"] = email, ["Senha"] = Senha }));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/admin/vagas")).StatusCode);

        using (var s = _b.Escopo())
            Assert.True((await s.ServiceProvider.GetRequiredService<Carreiras.Infra.Usuarios.GestaoUsuarios>().DesativarAsync(id, admin)).Ok);

        var r = await c.GetAsync("/admin/vagas");   // mesma sessão, agora inválida
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Contains("/admin/entrar", r.Headers.Location!.ToString());

        var novo = NovoCliente();
        var login = await novo.PostAsync("/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = await Token(novo, "/admin/entrar"), ["Email"] = email, ["Senha"] = Senha }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);   // volta a mostrar o formulário com erro
        Assert.Contains("incorretos", await login.Content.ReadAsStringAsync());
    }
}
