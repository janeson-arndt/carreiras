using Carreiras.Infra.Dados;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Auditoria;

public sealed record FiltroAuditoria(
    string? Tabela = null, int? UsuarioRhId = null, int? RegistroId = null,
    DateOnly? De = null, DateOnly? Ate = null, string? Campo = null);

public sealed record LinhaAuditoria(
    long Id, DateTime DataHoraUtc, int? UsuarioRhId, string Usuario, string Tabela, int RegistroId, string Registro,
    string Campo, string? ValorAntigo, string? ValorNovo);

public sealed record PaginaAuditoria(List<LinhaAuditoria> Itens, int Total, int Pagina, int Tamanho)
{
    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(Total / (double)Tamanho));
}

/// <summary>
/// Consulta somente leitura de LogAuditoria (SQL puro). Mostra o nome de quem agiu e um rótulo legível do registro,
/// e troca ids de campos de domínio (unidade, área, perfil...) pelo nome.
/// </summary>
public sealed class AuditoriaConsulta
{
    public const int TamanhoPadrao = 25;

    // campo -> tabela de domínio usada para traduzir o id (somente leitura; nomes fixos, nunca vindos do usuário)
    private static readonly Dictionary<string, string> Dominios = new()
    {
        ["UnidadeId"] = "Unidade", ["AreaId"] = "Area", ["TurnoId"] = "Turno", ["TipoContratoId"] = "TipoContrato",
        ["ModalidadeId"] = "Modalidade", ["ProgramaId"] = "Programa", ["CategoriaId"] = "BeneficioCategoria", ["PerfilId"] = "PerfilRh",
    };

    private readonly AppDbContext _db;
    public AuditoriaConsulta(AppDbContext db) => _db = db;

    public async Task<List<string>> TabelasAsync(CancellationToken ct = default) =>
        await _db.LogAuditoria.AsNoTracking().Select(l => l.Tabela).Distinct().OrderBy(t => t).ToListAsync(ct);

    public async Task<List<(int Id, string Nome)>> UsuariosAsync(CancellationToken ct = default) =>
        (await _db.UsuarioRhs.AsNoTracking().OrderBy(u => u.Nome).Select(u => new { u.Id, u.Nome }).ToListAsync(ct))
        .Select(u => (u.Id, u.Nome)).ToList();

    public async Task<PaginaAuditoria> ConsultarAsync(FiltroAuditoria f, int pagina, int tamanho = TamanhoPadrao, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 5, 200);
        var parametros = new
        {
            f.Tabela, f.UsuarioRhId, f.RegistroId,
            Campo = string.IsNullOrWhiteSpace(f.Campo) ? null : f.Campo.Trim(),
            De = f.De is null ? (DateTime?)null : ParaUtc(f.De.Value.ToDateTime(TimeOnly.MinValue)),
            AteExclusivo = f.Ate is null ? (DateTime?)null : ParaUtc(f.Ate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue)),
            Pular = (pagina - 1) * tamanho, Tamanho = tamanho,
        };
        const string where = @"
WHERE (@Tabela IS NULL OR l.Tabela = @Tabela)
  AND (@UsuarioRhId IS NULL OR l.UsuarioRhId = @UsuarioRhId)
  AND (@RegistroId IS NULL OR l.RegistroId = @RegistroId)
  AND (@Campo IS NULL OR l.Campo LIKE '%' + @Campo + '%')
  AND (@De IS NULL OR l.DataHora >= @De)
  AND (@AteExclusivo IS NULL OR l.DataHora < @AteExclusivo)";
        const string sql = @"
SELECT l.Id, l.DataHora AS DataHoraUtc, l.UsuarioRhId, COALESCE(u.Nome, N'Sistema') AS Usuario, l.Tabela, l.RegistroId,
       COALESCE(CASE l.Tabela
         WHEN 'Vaga' THEN v.Titulo WHEN 'Beneficio' THEN b.Nome WHEN 'Programa' THEN p.Nome
         WHEN 'UsuarioRh' THEN ur.Nome WHEN 'ConteudoInstitucional' THEN ci.Titulo END, N'#' + CONVERT(nvarchar(20), l.RegistroId)) AS Registro,
       l.Campo, l.ValorAntigo, l.ValorNovo
FROM dbo.LogAuditoria l
LEFT JOIN dbo.UsuarioRh u ON u.Id = l.UsuarioRhId
LEFT JOIN dbo.Vaga v ON l.Tabela = 'Vaga' AND v.Id = l.RegistroId
LEFT JOIN dbo.Beneficio b ON l.Tabela = 'Beneficio' AND b.Id = l.RegistroId
LEFT JOIN dbo.Programa p ON l.Tabela = 'Programa' AND p.Id = l.RegistroId
LEFT JOIN dbo.UsuarioRh ur ON l.Tabela = 'UsuarioRh' AND ur.Id = l.RegistroId
LEFT JOIN dbo.ConteudoInstitucional ci ON l.Tabela = 'ConteudoInstitucional' AND ci.Id = l.RegistroId" + where + @"
ORDER BY l.DataHora DESC, l.Id DESC
OFFSET @Pular ROWS FETCH NEXT @Tamanho ROWS ONLY;
SELECT COUNT(*) FROM dbo.LogAuditoria l" + where + ";";

        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        try
        {
            using var multi = await conn.QueryMultipleAsync(new CommandDefinition(sql, parametros, cancellationToken: ct));
            var itens = (await multi.ReadAsync<LinhaAuditoria>()).ToList();
            var total = await multi.ReadSingleAsync<int>();
            itens = await TraduzirAsync(conn, itens, ct);
            return new PaginaAuditoria(itens, total, pagina, tamanho);
        }
        finally { await _db.Database.CloseConnectionAsync(); }
    }

    private static async Task<List<LinhaAuditoria>> TraduzirAsync(System.Data.Common.DbConnection conn, List<LinhaAuditoria> itens, CancellationToken ct)
    {
        var campos = itens.Where(i => Dominios.ContainsKey(i.Campo)).Select(i => i.Campo).Distinct().ToList();
        if (campos.Count == 0) return itens;
        var nomes = new Dictionary<string, Dictionary<string, string>>();
        foreach (var campo in campos)
        {
            var tabela = Dominios[campo];   // vem do dicionário fixo acima
            var linhas = await conn.QueryAsync<(int Id, string Nome)>(new CommandDefinition($"SELECT Id, Nome FROM dbo.{tabela}", cancellationToken: ct));
            nomes[campo] = linhas.ToDictionary(l => l.Id.ToString(), l => l.Nome);
        }
        string? Mapa(string campo, string? v) =>
            v != null && nomes.TryGetValue(campo, out var d) && d.TryGetValue(v, out var n) ? n : v;
        return itens.Select(i => Dominios.ContainsKey(i.Campo)
            ? i with { ValorAntigo = Mapa(i.Campo, i.ValorAntigo), ValorNovo = Mapa(i.Campo, i.ValorNovo) }
            : i).ToList();
    }

    private static DateTime ParaUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local);
}
