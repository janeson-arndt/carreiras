using Carreiras.Dominio.Candidaturas;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Candidaturas;

/// <summary>Filtro da lista do RH: <see cref="Espontaneas"/> mostra só as sem vaga (banco de talentos); <see cref="VagaId"/> só as da vaga.</summary>
public sealed record FiltroCandidaturas(int? VagaId, bool Espontaneas, string? Status);

public sealed record CandidaturaLinha(
    int Id, string Candidato, bool Anonimizado, int? VagaId, string? VagaTitulo, decimal? Pretensao, DateTime CriadaEm, string Status);

public sealed record HistoricoItem(string Status, DateTime Em, string? Por);

public sealed record CandidaturaDetalhe(
    int Id, string Candidato, bool Anonimizado, string? Email, string? Telefone, string? Cidade, string? Uf,
    int? VagaId, string? VagaTitulo, decimal? Pretensao, DateTime CriadaEm, string Status,
    string? CurriculoNome, int? CurriculoKb, bool CurriculoDisponivel, List<HistoricoItem> Historico);

public sealed record ArquivoCurriculoRh(Stream Conteudo, string NomeExibicao, string ContentType);

/// <summary>Gestão das candidaturas pelo RH (Entrega 06): lista com filtros, detalhe, mudança de status com histórico e currículo.</summary>
public sealed class CandidaturaRhServico
{
    public const int PorPagina = 50;
    private readonly AppDbContext _db;
    private readonly ArmazenamentoCurriculos _arquivos;

    public CandidaturaRhServico(AppDbContext db, ArmazenamentoCurriculos arquivos) { _db = db; _arquivos = arquivos; }

    public async Task<(List<CandidaturaLinha> Itens, int Total)> ListarAsync(FiltroCandidaturas f, int pagina = 1, CancellationToken ct = default)
    {
        var q = _db.Candidaturas.AsNoTracking().AsQueryable();
        if (f.Espontaneas) q = q.Where(c => c.VagaId == null);
        else if (f.VagaId != null) q = q.Where(c => c.VagaId == f.VagaId);
        if (StatusCandidatura.Valido(f.Status)) q = q.Where(c => c.Status == f.Status);

        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(c => c.CriadaEm).ThenByDescending(c => c.Id)
            .Skip((Math.Max(pagina, 1) - 1) * PorPagina).Take(PorPagina)
            .Select(c => new CandidaturaLinha(
                c.Id, c.Candidato.AnonimizadoEm != null ? "Anonimizado" : c.Candidato.Nome, c.Candidato.AnonimizadoEm != null,
                c.VagaId, c.Vaga == null ? null : c.Vaga.Titulo, c.PretensaoSalarial, c.CriadaEm, c.Status))
            .ToListAsync(ct);
        return (itens, total);
    }

    public async Task<CandidaturaDetalhe?> ObterAsync(int id, CancellationToken ct = default)
    {
        var c = await _db.Candidaturas.AsNoTracking().Include(x => x.Candidato).Include(x => x.Vaga).Include(x => x.Curriculo)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return null;

        var anon = c.Candidato.AnonimizadoEm != null;
        var hist = await _db.CandidaturaStatusHists.AsNoTracking().Where(h => h.CandidaturaId == id)
            .OrderBy(h => h.AlteradoEm).ThenBy(h => h.Id)
            .Select(h => new HistoricoItem(h.Status, h.AlteradoEm, h.UsuarioRh == null ? null : h.UsuarioRh.Nome)).ToListAsync(ct);
        var cvOk = !anon && c.Curriculo.RemovidoEm == null && _arquivos.Existe(c.Curriculo.Caminho);

        return new CandidaturaDetalhe(
            c.Id, anon ? "Anonimizado" : c.Candidato.Nome, anon,
            anon ? null : c.Candidato.Email, anon ? null : c.Candidato.Telefone, anon ? null : c.Candidato.Cidade, anon ? null : c.Candidato.Uf,
            c.VagaId, c.Vaga?.Titulo, c.PretensaoSalarial, c.CriadaEm, c.Status,
            anon ? null : c.Curriculo.NomeArquivo, anon ? null : c.Curriculo.TamanhoKb, cvOk, hist);
    }

    /// <summary>Muda o status e grava o histórico (usuário e data) na mesma transação.</summary>
    public async Task<(bool Ok, string? Erro)> MudarStatusAsync(int id, string novo, int? usuarioRhId, CancellationToken ct = default)
    {
        if (!StatusCandidatura.Valido(novo)) return (false, "Situação inválida.");
        var c = await _db.Candidaturas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return (false, "Candidatura não encontrada.");
        if (c.Status == novo) return (false, "A candidatura já está nessa situação.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        c.Status = novo;
        _db.CandidaturaStatusHists.Add(new CandidaturaStatusHist
        {
            CandidaturaId = c.Id, Status = novo, AlteradoEm = DateTime.UtcNow, UsuarioRhId = usuarioRhId,
        });
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (true, null);
    }

    /// <summary>
    /// Abre o currículo da candidatura. Nulo se a candidatura não existe, o candidato foi anonimizado, o arquivo foi
    /// removido ou não está no disco. A autorização (RH autenticado) é da página que chama.
    /// </summary>
    public async Task<ArquivoCurriculoRh?> AbrirCurriculoAsync(int candidaturaId, CancellationToken ct = default)
    {
        var c = await _db.Candidaturas.AsNoTracking().Include(x => x.Candidato).Include(x => x.Curriculo)
            .FirstOrDefaultAsync(x => x.Id == candidaturaId, ct);
        if (c == null || c.Candidato.AnonimizadoEm != null || c.Curriculo.RemovidoEm != null) return null;

        var fluxo = _arquivos.Abrir(c.Curriculo.Caminho);
        if (fluxo == null) return null;
        var tipo = c.Curriculo.Caminho.EndsWith(".pdf", StringComparison.Ordinal)
            ? "application/pdf"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        return new ArquivoCurriculoRh(fluxo, Dominio.Candidaturas.ArquivoCurriculo.NomeParaExibicao(c.Curriculo.NomeArquivo), tipo);
    }

    /// <summary>Vagas para o filtro (todas, com a contagem de candidaturas) e quantas espontâneas existem.</summary>
    public async Task<(List<(int Id, string Titulo, int Candidaturas)> Vagas, int Espontaneas)> OpcoesFiltroAsync(CancellationToken ct = default)
    {
        var vagas = await _db.Vagas.AsNoTracking().OrderBy(v => v.Titulo)
            .Select(v => new { v.Id, v.Titulo, N = v.Candidaturas.Count }).ToListAsync(ct);
        var esp = await _db.Candidaturas.CountAsync(c => c.VagaId == null, ct);
        return (vagas.Select(v => (v.Id, v.Titulo, v.N)).ToList(), esp);
    }
}
