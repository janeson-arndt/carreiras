using System.Text.RegularExpressions;
using Carreiras.Dominio.Usuarios;
using Carreiras.Infra;
using Carreiras.Infra.Auditoria;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Email;
using Carreiras.Infra.Identidade;
using Carreiras.Infra.Usuarios;
using Carreiras.Infra.Vagas;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

/// <summary>
/// Banco SQL Server REAL (não em memória), recriado a cada execução a partir de db/001 e db/002.
/// Variável de ambiente exigida: CARREIRAS_TESTE_ADMIN_CS = conexão com permissão de criar banco (ex.: sa).
/// O banco usado é RH_METISA_TESTE; o RH_METISA (desenvolvimento) nunca é tocado.
/// </summary>
public sealed class BancoTeste : IAsyncLifetime
{
    public const string Nome = "RH_METISA_TESTE";
    public string ConexaoApp { get; private set; } = "";
    public string ConexaoAdmin { get; private set; } = "";
    public UsuarioAuditoriaFixo Ator { get; } = new();
    private ServiceProvider _raiz = null!;

    public async Task InitializeAsync()
    {
        var admin = Environment.GetEnvironmentVariable("CARREIRAS_TESTE_ADMIN_CS")
            ?? throw new InvalidOperationException(
                "Defina CARREIRAS_TESTE_ADMIN_CS (conexão com permissão de criar banco) para rodar os testes de integração.");
        var b = new SqlConnectionStringBuilder(admin) { InitialCatalog = "master", TrustServerCertificate = true };
        await Executar(b.ConnectionString, $"IF DB_ID('{Nome}') IS NOT NULL BEGIN ALTER DATABASE {Nome} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {Nome}; END; CREATE DATABASE {Nome};");

        b.InitialCatalog = Nome;
        ConexaoAdmin = b.ConnectionString;
        ConexaoApp = ConexaoAdmin;

        var raizRepo = AchaRaiz();
        foreach (var arq in new[] { "carreiras_entrega01.sql", "002_usuarios.sql" })
        {
            var sql = File.ReadAllText(Path.Combine(raizRepo, "db", arq)).Replace("USE RH_METISA;", $"USE {Nome};");
            foreach (var lote in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(lote)) await Executar(ConexaoAdmin, lote);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Carreiras"] = ConexaoApp }).Build();
        services.AddCarreirasInfra(config);
        services.AddSingleton(Ator);
        services.AddScoped<IUsuarioAuditoria>(sp => sp.GetRequiredService<UsuarioAuditoriaFixo>());
        services.AddIdentityCore<IdentityUser>(o =>
        {
            o.Password.RequiredLength = 10;
            o.Password.RequireNonAlphanumeric = false;
            o.User.RequireUniqueEmail = true;
        }).AddEntityFrameworkStores<AuthDbContext>().AddDefaultTokenProviders();
        services.AddSingleton<EmailFalso>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<EmailFalso>());
        services.AddScoped<GestaoUsuarios>();
        services.AddScoped<VagaServico>();
        services.AddScoped<Carreiras.Infra.Cadastros.CadastroServico>();
        services.AddScoped<Carreiras.Infra.Cadastros.ConteudoServico>();
        services.AddScoped<Carreiras.Infra.Auditoria.AuditoriaConsulta>();
        services.AddScoped<Carreiras.Infra.Vitrine.VitrineServico>();
        _raiz = services.BuildServiceProvider();

        using var s = _raiz.CreateScope();
        await s.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() { _raiz?.Dispose(); return Task.CompletedTask; }

    public IServiceScope Escopo() => _raiz.CreateScope();
    public EmailFalso Emails => _raiz.GetRequiredService<EmailFalso>();

    /// <summary>Cria um usuário do RH com senha já definida (convite aceito), ou sem senha (convite pendente).</summary>
    public async Task<int> CriarUsuarioAsync(string perfil, bool ativo = true, bool conviteAceito = true, string? email = null)
    {
        using var s = Escopo();
        var um = s.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        email ??= $"{Guid.NewGuid():N}@teste.local";
        var identity = new IdentityUser { UserName = email, Email = email, EmailConfirmed = conviteAceito, LockoutEnabled = true };
        var r = conviteAceito ? await um.CreateAsync(identity, "SenhaForte2026x") : await um.CreateAsync(identity);
        if (!r.Succeeded) throw new InvalidOperationException(string.Join(";", r.Errors.Select(e => e.Description)));
        var perfilId = await db.PerfilRhs.Where(p => p.Nome == perfil).Select(p => p.Id).SingleAsync();
        var u = new UsuarioRh { Nome = "Teste " + perfil, Email = email, PerfilId = perfilId, Ativo = ativo, IdentityUserId = identity.Id };
        db.UsuarioRhs.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    /// <summary>Deixa só os usuários informados ativos (os demais são desativados), para isolar cenários.</summary>
    public async Task DesativarTodosAsync()
    {
        await Executar(ConexaoAdmin, "UPDATE dbo.UsuarioRh SET Ativo = 0");
    }

    public static async Task Executar(string conexao, string sql)
    {
        await using var c = new SqlConnection(conexao);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Executa SQL com parâmetros (para dados com aspas, tags e acentos).</summary>
    public async Task ExecutarAsync(string sql, params (string Nome, object? Valor)[] parametros)
    {
        await using var c = new SqlConnection(ConexaoAdmin);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        foreach (var (nome, valor) in parametros) cmd.Parameters.AddWithValue(nome, valor ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<T> Escalar<T>(string sql)
    {
        await using var c = new SqlConnection(ConexaoAdmin);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    private static string AchaRaiz()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "db"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("Pasta db/ não encontrada.");
    }
}

public sealed class EmailFalso : IEmailSender
{
    public List<(string Para, string Assunto, string Corpo)> Enviados { get; } = new();
    public Task EnviarAsync(string para, string assunto, string corpoTexto, CancellationToken ct = default)
    {
        lock (Enviados) Enviados.Add((para, assunto, corpoTexto));
        return Task.CompletedTask;
    }
}

[CollectionDefinition("banco")]
public sealed class BancoColecao : ICollectionFixture<BancoTeste> { }
