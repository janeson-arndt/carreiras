using Carreiras.Infra.Auditoria;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Identidade;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Infra;

public static class InfraServicos
{
    public static IServiceCollection AddCarreirasInfra(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Carreiras")
            ?? throw new InvalidOperationException(
                "Defina ConnectionStrings:Carreiras (dotnet user-secrets). Nunca grave a senha no repositório.");

        services.AddScoped<AuditoriaInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) => o
            .UseSqlServer(cs)
            .AddInterceptors(sp.GetRequiredService<AuditoriaInterceptor>()));
        services.AddDbContext<AuthDbContext>(o => o.UseSqlServer(cs));

        services.Configure<Candidaturas.CurriculosOpcoes>(config.GetSection("Curriculos"));
        services.Configure<Candidaturas.CandidaturaOpcoes>(config.GetSection("Candidatura"));
        services.Configure<Candidaturas.RetencaoOpcoes>(config.GetSection("Retencao"));
        services.AddSingleton<Candidaturas.ArmazenamentoCurriculos>();
        services.AddScoped<Candidaturas.CandidaturaServico>();
        services.AddScoped<Candidaturas.RetencaoServico>();
        return services;
    }
}
