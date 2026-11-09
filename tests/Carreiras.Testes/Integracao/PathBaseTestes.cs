using System.Net;
using System.Text.RegularExpressions;
using Carreiras.Dominio.Usuarios;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Carreiras.Testes.Integracao;

/// <summary>O portal servido sob um prefixo (http://localhost:5129/rhmetisa): links, estáticos, cookie, login e redirecionamentos.</summary>
[Collection("banco")]
public class PathBaseTestes
{
    private const string Base = "/rhmetisa";
    private const string Senha = "SenhaForte2026x";
    private readonly BancoTeste _b;
    public PathBaseTestes(BancoTeste b) => _b = b;

    private static async Task<string> Token(HttpClient c, string url)
    {
        var html = await (await c.GetAsync(url)).Content.ReadAsStringAsync();
        return Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    }

    [Fact]
    public async Task Portal_funciona_inteiro_sob_o_prefixo_rhmetisa()
    {
        var antigo = Environment.GetEnvironmentVariable("PathBase");
        Environment.SetEnvironmentVariable("ConnectionStrings__Carreiras", _b.ConexaoApp);
        Environment.SetEnvironmentVariable("PathBase", Base);   // restaurado no finally: o ambiente é do processo todo
        try
        {
            using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(h => h.UseEnvironment("Development"));
            var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // sem login: vai para a entrada, mantendo o prefixo, e a volta também
            var anon = await c.GetAsync($"{Base}/admin");
            Assert.Equal(HttpStatusCode.Redirect, anon.StatusCode);
            Assert.StartsWith($"{Base}/admin/entrar", anon.Headers.Location!.OriginalString.Replace("http://localhost", ""));
            Assert.Contains("ReturnUrl=%2Frhmetisa%2Fadmin", anon.Headers.Location.OriginalString);

            // a página de entrada, com tudo apontando para dentro do prefixo
            var pagina = await c.GetAsync($"{Base}/admin/entrar");
            Assert.Equal(HttpStatusCode.OK, pagina.StatusCode);
            var html = await pagina.Content.ReadAsStringAsync();
            Assert.DoesNotMatch(@"(href|src|action)=""/(?!rhmetisa)[a-z]", html);   // nenhum caminho fora do prefixo

            // estáticos (com hash no nome em desenvolvimento) carregam sob o prefixo
            var estaticos = Regex.Matches(html, @"(?:href|src)=""(" + Base + @"/(?:css|lib|js)/[^""]+)""").Select(m => m.Groups[1].Value).ToList();
            Assert.Contains(estaticos, e => e.Contains("/css/carreiras"));
            Assert.Contains(estaticos, e => e.Contains("/lib/bootstrap/"));
            foreach (var e in estaticos)
                Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(e)).StatusCode);

            // fora do prefixo não existe; a raiz leva ao portal
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/admin/entrar")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/admin/vagas")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/css/carreiras.css")).StatusCode);
            var raiz = await c.GetAsync("/");
            Assert.Equal(HttpStatusCode.Redirect, raiz.StatusCode);
            Assert.Equal($"{Base}/", raiz.Headers.Location!.OriginalString);   // a raiz leva à vitrine, sob o prefixo

            // a vitrine pública também vive sob o prefixo, com links e imagens dentro dele
            var vitrine = await c.GetAsync($"{Base}/");
            Assert.Equal(HttpStatusCode.OK, vitrine.StatusCode);
            var vh = await vitrine.Content.ReadAsStringAsync();
            Assert.Contains($"href=\"{Base}/vagas\"", vh);
            Assert.DoesNotMatch(@"(href|src|action)=""/(?!rhmetisa)[a-z]", vh);
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{Base}/img/vitrine/vista-aerea.jpg")).StatusCode);

            // login: volta para dentro do prefixo e o cookie vale só para ele
            var email = $"base-{Guid.NewGuid():N}@teste.local";
            await _b.CriarUsuarioAsync(Perfis.Administrador, email: email);
            var login = await c.PostAsync($"{Base}/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = await Token(c, $"{Base}/admin/entrar"), ["Email"] = email, ["Senha"] = Senha }));
            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal($"{Base}/admin", login.Headers.Location!.OriginalString);
            var cookie = login.Headers.GetValues("Set-Cookie").First(x => x.StartsWith("carreiras.rh"));
            Assert.Contains($"path={Base}", cookie, StringComparison.OrdinalIgnoreCase);

            // navegação autenticada: links do menu e do voltar-à-lista seguem no prefixo
            var vagas = await c.GetAsync($"{Base}/admin/vagas");
            Assert.Equal(HttpStatusCode.OK, vagas.StatusCode);
            var v = await vagas.Content.ReadAsStringAsync();
            Assert.Contains($"href=\"{Base}/admin/usuarios\"", v);
            Assert.Contains($"href=\"{Base}/admin/auditoria\"", v);
            Assert.DoesNotMatch(@"(href|src|action)=""/(?!rhmetisa)[a-z]", v);

            // acesso negado e saída também respeitam o prefixo
            var sair = await c.PostAsync($"{Base}/admin/sair", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = await Token(c, $"{Base}/admin/vagas") }));
            Assert.Equal(HttpStatusCode.Redirect, sair.StatusCode);
            Assert.StartsWith($"{Base}/admin/entrar", sair.Headers.Location!.OriginalString);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PathBase", antigo);
        }
    }

    [Fact]
    public async Task Link_do_convite_inclui_o_prefixo()
    {
        var antigo = Environment.GetEnvironmentVariable("PathBase");
        var pasta = Path.Combine(Path.GetTempPath(), "carreiras-pb-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ConnectionStrings__Carreiras", _b.ConexaoApp);
        Environment.SetEnvironmentVariable("PathBase", Base);
        Environment.SetEnvironmentVariable("Email__PastaSaida", pasta);
        try
        {
            using var f = new WebApplicationFactory<Program>().WithWebHostBuilder(h => h.UseEnvironment("Development"));
            var c = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var adminEmail = $"base-adm-{Guid.NewGuid():N}@teste.local";
            await _b.CriarUsuarioAsync(Perfis.Administrador, email: adminEmail);
            await c.PostAsync($"{Base}/admin/entrar", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["__RequestVerificationToken"] = await Token(c, $"{Base}/admin/entrar"), ["Email"] = adminEmail, ["Senha"] = Senha }));

            var novo = $"base-novo-{Guid.NewGuid():N}@teste.local";
            await c.PostAsync($"{Base}/admin/usuarios/novo", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await Token(c, $"{Base}/admin/usuarios/novo"),
                ["Nome"] = "Prefixo", ["Email"] = novo, ["Perfil"] = Perfis.Leitura,
            }));

            var arquivo = Directory.GetFiles(pasta).Single(a => File.ReadAllText(a).StartsWith($"Para: {novo}\n"));
            var link = Regex.Match(File.ReadAllText(arquivo), @"http://\S+").Value;
            Assert.Contains($"{Base}/admin/definir-senha?uid=", link);

            // e o link funciona de verdade sob o prefixo
            var abre = await Task.FromResult(await f.CreateClient().GetAsync(link));
            Assert.Equal(HttpStatusCode.OK, abre.StatusCode);
            Assert.DoesNotContain("expirou", WebUtility.HtmlDecode(await abre.Content.ReadAsStringAsync()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PathBase", antigo);
            Environment.SetEnvironmentVariable("Email__PastaSaida", null);
            try { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); } catch { /* temporário */ }
        }
    }
}
