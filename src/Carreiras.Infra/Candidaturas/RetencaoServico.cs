using Carreiras.Infra.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carreiras.Infra.Candidaturas;

public sealed class RetencaoOpcoes
{
    /// <summary>Liga o job diário. Fica desligado por padrão: ligue só em um ambiente (ou use o agendador do servidor).</summary>
    public bool Habilitado { get; set; }
    public int DiasPendenteNaoConfirmado { get; set; } = 7;
}

/// <summary>
/// Limpeza de dados pessoais (LGPD): candidaturas nunca confirmadas, candidatos com retenção vencida (anonimização)
/// e os arquivos de currículo marcados como removidos.
/// </summary>
public sealed class RetencaoServico
{
    private readonly AppDbContext _db;
    private readonly ArmazenamentoCurriculos _arquivos;
    private readonly RetencaoOpcoes _op;
    private readonly ILogger<RetencaoServico> _log;

    public RetencaoServico(AppDbContext db, ArmazenamentoCurriculos arquivos, IOptions<RetencaoOpcoes> op, ILogger<RetencaoServico> log)
    {
        _db = db; _arquivos = arquivos; _op = op.Value; _log = log;
    }

    public async Task<(int Anonimizados, int ArquivosApagados)> ExecutarAsync(CancellationToken ct = default)
    {
        await _db.Database.ExecuteSqlRawAsync("EXEC dbo.usp_ExpirarCandidaturasNaoConfirmadas @dias = {0}", new object[] { _op.DiasPendenteNaoConfirmado }, ct);
        var antes = await _db.Candidatos.CountAsync(c => c.AnonimizadoEm != null, ct);
        await _db.Database.ExecuteSqlRawAsync("EXEC dbo.usp_AnonimizarCandidatosExpirados", ct);
        var depois = await _db.Candidatos.CountAsync(c => c.AnonimizadoEm != null, ct);

        var apagados = 0;
        var removidos = await _db.Curriculos.AsNoTracking().Where(c => c.RemovidoEm != null).Select(c => c.Caminho).ToListAsync(ct);
        foreach (var nome in removidos)
        {
            if (!_arquivos.Existe(nome)) continue;
            _arquivos.Apagar(nome);
            apagados++;
        }
        _log.LogInformation("Retenção: {Anonimizados} candidatos anonimizados, {Arquivos} arquivos apagados", depois - antes, apagados);
        return (depois - antes, apagados);
    }
}

public sealed class RetencaoDiaria : BackgroundService
{
    private readonly IServiceScopeFactory _fabrica;
    private readonly RetencaoOpcoes _op;
    private readonly ILogger<RetencaoDiaria> _log;

    public RetencaoDiaria(IServiceScopeFactory fabrica, IOptions<RetencaoOpcoes> op, ILogger<RetencaoDiaria> log)
    {
        _fabrica = fabrica; _op = op.Value; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_op.Habilitado) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var escopo = _fabrica.CreateScope();
                await escopo.ServiceProvider.GetRequiredService<RetencaoServico>().ExecutarAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogError(e, "Falha na rotina diária de retenção");
            }
            try { await Task.Delay(TimeSpan.FromHours(24), ct); } catch (OperationCanceledException) { }
        }
    }
}
