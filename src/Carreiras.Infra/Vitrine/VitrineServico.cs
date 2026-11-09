using Carreiras.Infra.Dados;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Vitrine;

public sealed record FiltroPublico(int? UnidadeId = null, int? TurnoId = null, int? TipoContratoId = null, int? AreaId = null)
{
    public bool Ativo => UnidadeId.HasValue || TurnoId.HasValue || TipoContratoId.HasValue || AreaId.HasValue;
}

public sealed record VagaPublicaResumo(string Slug, string Titulo, string Cidade, string Uf, string Turno, string Contrato, string Area);

public sealed record OpcaoFiltro(int Id, string Rotulo, int Quantidade);

public sealed record OpcoesFiltro(List<OpcaoFiltro> Unidades, List<OpcaoFiltro> Turnos, List<OpcaoFiltro> Contratos, List<OpcaoFiltro> Areas);

public sealed record UnidadePublica(
    int Id, string Nome, string Tipo, string Endereco, string? Bairro, string Cidade, string Uf, string? Cep, string? Telefone, int Vagas);

/// <summary>Exemplo = o item é conteúdo de exemplo (Ficticio = 1) e a vitrine avisa.</summary>
public sealed record BeneficioPublico(string Nome, string? Descricao, string? Icone, bool Exemplo);
public sealed record CategoriaBeneficios(string Nome, List<BeneficioPublico> Itens);
public sealed record ProgramaPublico(string Nome, string? Descricao, string? PublicoAlvo, bool Exemplo);
public sealed record ConteudoPublico(string Secao, string Titulo, string Texto, bool Exemplo);
public sealed record RequisitoPublico(string Texto, bool Obrigatorio);

public sealed class VagaPublicaDetalhe
{
    public int Id { get; init; }
    public string Slug { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Descricao { get; init; } = "";
    public DateTime? PublicadaEm { get; init; }
    public string Area { get; init; } = "";
    public string Turno { get; init; } = "";
    public string Contrato { get; init; } = "";
    public string Modalidade { get; init; } = "";
    public string? Programa { get; init; }
    public string UnidadeNome { get; init; } = "";
    public string UnidadeCidade { get; init; } = "";
    public string UnidadeUf { get; init; } = "";
    public string UnidadeEndereco { get; init; } = "";
    public string? UnidadeBairro { get; init; }
    public string? UnidadeCep { get; init; }
    /// <summary>Só vem preenchida quando a vaga marca "mostrar a faixa salarial" e tem os dois valores (RN04).</summary>
    public decimal? SalarioMin { get; init; }
    public decimal? SalarioMax { get; init; }
    public List<string> Atividades { get; init; } = new();
    public List<RequisitoPublico> Requisitos { get; init; } = new();
    public List<string> Diferenciais { get; init; } = new();
    public List<string> Beneficios { get; init; } = new();
}

/// <summary>
/// Consultas do portal público (somente leitura, SQL puro). RN03: só vagas PUBLICADA existem aqui;
/// rascunho, pausada e encerrada nunca aparecem, nem pela URL.
/// </summary>
public sealed class VitrineServico
{
    private const string Publicada = "PUBLICADA";
    private readonly AppDbContext _db;
    public VitrineServico(AppDbContext db) => _db = db;

    private async Task<T> ComConexaoAsync<T>(Func<System.Data.Common.DbConnection, Task<T>> acao, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        try { return await acao(conn); }
        finally { await _db.Database.CloseConnectionAsync(); }
    }

    public Task<List<VagaPublicaResumo>> ListarVagasAsync(FiltroPublico f, CancellationToken ct = default) =>
        ComConexaoAsync(async c => (await c.QueryAsync<VagaPublicaResumo>(new CommandDefinition(@"
SELECT v.Slug, v.Titulo, u.Cidade, u.Uf, t.Nome AS Turno, tc.Nome AS Contrato, a.Nome AS Area
FROM dbo.Vaga v
JOIN dbo.Unidade u ON u.Id = v.UnidadeId
JOIN dbo.Turno t ON t.Id = v.TurnoId
JOIN dbo.TipoContrato tc ON tc.Id = v.TipoContratoId
JOIN dbo.Area a ON a.Id = v.AreaId
WHERE v.Status = 'PUBLICADA'
  AND (@UnidadeId IS NULL OR v.UnidadeId = @UnidadeId)
  AND (@TurnoId IS NULL OR v.TurnoId = @TurnoId)
  AND (@TipoContratoId IS NULL OR v.TipoContratoId = @TipoContratoId)
  AND (@AreaId IS NULL OR v.AreaId = @AreaId)
ORDER BY v.PublicadaEm DESC, v.Titulo, v.Id",
            new { f.UnidadeId, f.TurnoId, f.TipoContratoId, f.AreaId }, cancellationToken: ct))).ToList(), ct);

    /// <summary>Opções dos filtros, com a quantidade de vagas publicadas de cada uma (sem considerar os outros filtros).</summary>
    public Task<OpcoesFiltro> OpcoesAsync(CancellationToken ct = default) =>
        ComConexaoAsync(async c =>
        {
            async Task<List<OpcaoFiltro>> Q(string sql) =>
                (await c.QueryAsync<OpcaoFiltro>(new CommandDefinition(sql, cancellationToken: ct))).ToList();

            var unidades = await Q(@"
SELECT u.Id, u.Cidade + N' (' + u.Uf + N')' AS Rotulo, COUNT(v.Id) AS Quantidade
FROM dbo.Unidade u LEFT JOIN dbo.Vaga v ON v.UnidadeId = u.Id AND v.Status = 'PUBLICADA'
WHERE u.Ativo = 1 GROUP BY u.Id, u.Cidade, u.Uf ORDER BY u.Id");
            var turnos = await Q(@"
SELECT t.Id, t.Nome AS Rotulo, COUNT(*) AS Quantidade
FROM dbo.Vaga v JOIN dbo.Turno t ON t.Id = v.TurnoId WHERE v.Status = 'PUBLICADA' GROUP BY t.Id, t.Nome ORDER BY t.Id");
            var contratos = await Q(@"
SELECT tc.Id, tc.Nome AS Rotulo, COUNT(*) AS Quantidade
FROM dbo.Vaga v JOIN dbo.TipoContrato tc ON tc.Id = v.TipoContratoId WHERE v.Status = 'PUBLICADA' GROUP BY tc.Id, tc.Nome ORDER BY tc.Id");
            var areas = await Q(@"
SELECT a.Id, a.Nome AS Rotulo, COUNT(*) AS Quantidade
FROM dbo.Vaga v JOIN dbo.Area a ON a.Id = v.AreaId WHERE v.Status = 'PUBLICADA' GROUP BY a.Id, a.Nome ORDER BY a.Nome");
            return new OpcoesFiltro(unidades, turnos, contratos, areas);
        }, ct);

    public Task<List<UnidadePublica>> UnidadesAsync(CancellationToken ct = default) =>
        ComConexaoAsync(async c => (await c.QueryAsync<UnidadePublica>(new CommandDefinition(@"
SELECT u.Id, u.Nome,
       CASE u.Tipo WHEN 'MATRIZ' THEN N'Matriz' WHEN 'CENTRO_DISTRIBUICAO' THEN N'Centro de distribuição' ELSE N'Escritório comercial' END AS Tipo,
       u.Endereco, u.Bairro, u.Cidade, u.Uf, u.Cep, u.Telefone,
       (SELECT COUNT(*) FROM dbo.Vaga v WHERE v.UnidadeId = u.Id AND v.Status = 'PUBLICADA') AS Vagas
FROM dbo.Unidade u WHERE u.Ativo = 1 ORDER BY u.Id", cancellationToken: ct))).ToList(), ct);

    public async Task<List<CategoriaBeneficios>> BeneficiosPorCategoriaAsync(CancellationToken ct = default)
    {
        var linhas = await ComConexaoAsync(async c => (await c.QueryAsync<(int CategoriaId, string Categoria, string Nome, string? Descricao, string? Icone, bool Ficticio)>(
            new CommandDefinition(@"
SELECT c.Id AS CategoriaId, c.Nome AS Categoria, b.Nome, b.DescricaoCurta AS Descricao, b.Icone, b.Ficticio
FROM dbo.BeneficioCategoria c JOIN dbo.Beneficio b ON b.CategoriaId = c.Id
WHERE b.Ativo = 1 ORDER BY c.Ordem, c.Id, b.Ordem, b.Nome", cancellationToken: ct))).ToList(), ct);
        return linhas.GroupBy(l => (l.CategoriaId, l.Categoria))
            .Select(g => new CategoriaBeneficios(g.Key.Categoria,
                g.Select(l => new BeneficioPublico(l.Nome, l.Descricao, l.Icone, l.Ficticio)).ToList())).ToList();
    }

    public Task<List<ProgramaPublico>> ProgramasAsync(CancellationToken ct = default) =>
        ComConexaoAsync(async c => (await c.QueryAsync<ProgramaPublico>(new CommandDefinition(
            "SELECT Nome, Descricao, PublicoAlvo, Ficticio AS Exemplo FROM dbo.Programa WHERE Ativo = 1 ORDER BY Id", cancellationToken: ct))).ToList(), ct);

    public Task<List<ConteudoPublico>> ConteudosAsync(CancellationToken ct = default) =>
        ComConexaoAsync(async c => (await c.QueryAsync<ConteudoPublico>(new CommandDefinition(
            "SELECT Secao, Titulo, Texto, Ficticio AS Exemplo FROM dbo.ConteudoInstitucional WHERE Publicado = 1 ORDER BY Ordem, Id", cancellationToken: ct))).ToList(), ct);

    /// <summary>Devolve a vaga só se ela estiver PUBLICADA; qualquer outra situação é como se não existisse.</summary>
    public Task<VagaPublicaDetalhe?> ObterVagaAsync(string slug, CancellationToken ct = default) =>
        ComConexaoAsync<VagaPublicaDetalhe?>(async c =>
        {
            var v = await c.QuerySingleOrDefaultAsync(new CommandDefinition(@"
SELECT v.Id, v.Slug, v.Titulo, v.Descricao, v.PublicadaEm, v.SalarioMin, v.SalarioMax, v.ExibirSalario,
       a.Nome AS Area, t.Nome AS Turno, tc.Nome AS Contrato, m.Nome AS Modalidade, p.Nome AS Programa,
       u.Nome AS UnidadeNome, u.Cidade AS UnidadeCidade, u.Uf AS UnidadeUf, u.Endereco AS UnidadeEndereco,
       u.Bairro AS UnidadeBairro, u.Cep AS UnidadeCep
FROM dbo.Vaga v
JOIN dbo.Unidade u ON u.Id = v.UnidadeId
JOIN dbo.Area a ON a.Id = v.AreaId
JOIN dbo.Turno t ON t.Id = v.TurnoId
JOIN dbo.TipoContrato tc ON tc.Id = v.TipoContratoId
JOIN dbo.Modalidade m ON m.Id = v.ModalidadeId
LEFT JOIN dbo.Programa p ON p.Id = v.ProgramaId
WHERE v.Slug = @slug AND v.Status = 'PUBLICADA'", new { slug }, cancellationToken: ct));
            if (v == null) return null;
            int id = v.Id;

            using var m = await c.QueryMultipleAsync(new CommandDefinition(@"
SELECT Texto FROM dbo.VagaAtividade WHERE VagaId = @id ORDER BY Id;
SELECT Texto, Obrigatorio FROM dbo.VagaRequisito WHERE VagaId = @id ORDER BY Id;
SELECT Texto FROM dbo.VagaDiferencial WHERE VagaId = @id ORDER BY Id;
SELECT b.Nome FROM dbo.VagaBeneficio vb
  JOIN dbo.Beneficio b ON b.Id = vb.BeneficioId JOIN dbo.BeneficioCategoria c ON c.Id = b.CategoriaId
WHERE vb.VagaId = @id ORDER BY c.Ordem, b.Ordem, b.Nome;", new { id }, cancellationToken: ct));
            var atividades = (await m.ReadAsync<string>()).ToList();
            var requisitos = (await m.ReadAsync<RequisitoPublico>()).ToList();
            var diferenciais = (await m.ReadAsync<string>()).ToList();
            var beneficios = (await m.ReadAsync<string>()).ToList();

            decimal? min = v.SalarioMin, max = v.SalarioMax;
            var mostrar = (bool)v.ExibirSalario && min.HasValue && max.HasValue;   // RN04
            return new VagaPublicaDetalhe
            {
                Id = id, Slug = v.Slug, Titulo = v.Titulo, Descricao = v.Descricao, PublicadaEm = v.PublicadaEm,
                Area = v.Area, Turno = v.Turno, Contrato = v.Contrato, Modalidade = v.Modalidade, Programa = v.Programa,
                UnidadeNome = v.UnidadeNome, UnidadeCidade = v.UnidadeCidade, UnidadeUf = v.UnidadeUf,
                UnidadeEndereco = v.UnidadeEndereco, UnidadeBairro = v.UnidadeBairro, UnidadeCep = v.UnidadeCep,
                SalarioMin = mostrar ? min : null, SalarioMax = mostrar ? max : null,
                Atividades = atividades, Requisitos = requisitos, Diferenciais = diferenciais, Beneficios = beneficios,
            };
        }, ct);
}
