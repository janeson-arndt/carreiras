using Carreiras.Infra.Dados;
using Carreiras.Infra.Identidade;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Carreiras.Infra;

// Usadas apenas pelo `dotnet ef`. A conexão (com permissão de DDL) vem da variável de ambiente
// CARREIRAS_MIGRATION_CS, nunca de arquivo versionado.
internal static class ConexaoMigracao
{
    public static string Obter() =>
        Environment.GetEnvironmentVariable("CARREIRAS_MIGRATION_CS")
        ?? throw new InvalidOperationException("Defina CARREIRAS_MIGRATION_CS para usar o dotnet ef.");
}

internal sealed class AppDbContextFabrica : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConexaoMigracao.Obter()).Options);
}

internal sealed class AuthDbContextFabrica : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(ConexaoMigracao.Obter()).Options);
}
