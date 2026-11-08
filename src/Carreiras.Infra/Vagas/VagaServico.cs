using Carreiras.Dominio.Vagas;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Usuarios;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Vagas;

public sealed class RequisitoForm
{
    public string Texto { get; set; } = "";
    public bool Obrigatorio { get; set; } = true;
}

public sealed class VagaForm
{
    public int? Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public int UnidadeId { get; set; }
    public int AreaId { get; set; }
    public int TurnoId { get; set; }
    public int TipoContratoId { get; set; }
    public int ModalidadeId { get; set; }
    public int? ProgramaId { get; set; }
    public decimal? SalarioMin { get; set; }
    public decimal? SalarioMax { get; set; }
    public bool ExibirSalario { get; set; }
    public List<int> BeneficioIds { get; set; } = new();
    public List<RequisitoForm> Requisitos { get; set; } = new();
    public List<string> Atividades { get; set; } = new();
    public List<string> Diferenciais { get; set; } = new();
}

public sealed record VagaLinha(
    int Id, string Slug, string Titulo, string Status, string Unidade, string Area, string Turno,
    DateTime? PublicadaEm, int Candidaturas);

public sealed record FiltroVagas(string? Busca = null, string? Status = null, int? UnidadeId = null);

/// <summary>Vagas do portal do RH. Escritas via EF Core (passam pelo interceptador de auditoria); listagem em SQL puro.</summary>
public sealed class VagaServico
{
    private readonly AppDbContext _db;
    public VagaServico(AppDbContext db) => _db = db;

    public async Task<List<VagaLinha>> ListarAsync(FiltroVagas f, CancellationToken ct = default)
    {
        const string sql = @"
SELECT v.Id, v.Slug, v.Titulo, v.Status, u.Nome AS Unidade, a.Nome AS Area, t.Nome AS Turno, v.PublicadaEm,
       (SELECT COUNT(*) FROM dbo.Candidatura c WHERE c.VagaId = v.Id) AS Candidaturas
FROM dbo.Vaga v
JOIN dbo.Unidade u ON u.Id = v.UnidadeId
JOIN dbo.Area a ON a.Id = v.AreaId
JOIN dbo.Turno t ON t.Id = v.TurnoId
WHERE (@Status IS NULL OR v.Status = @Status)
  AND (@UnidadeId IS NULL OR v.UnidadeId = @UnidadeId)
  AND (@Busca IS NULL OR v.Titulo LIKE '%' + @Busca + '%')
ORDER BY CASE v.Status WHEN 'PUBLICADA' THEN 0 WHEN 'PAUSADA' THEN 1 WHEN 'RASCUNHO' THEN 2 ELSE 3 END, v.Titulo";
        var cmd = new CommandDefinition(sql, new
        {
            Status = string.IsNullOrWhiteSpace(f.Status) ? null : f.Status,
            f.UnidadeId,
            Busca = string.IsNullOrWhiteSpace(f.Busca) ? null : f.Busca.Trim(),
        }, cancellationToken: ct);
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        try { return (await conn.QueryAsync<VagaLinha>(cmd)).ToList(); }
        finally { await _db.Database.CloseConnectionAsync(); }
    }

    public async Task<(VagaForm Form, string Status)?> ObterAsync(int id, CancellationToken ct = default)
    {
        var v = await _db.Vagas.AsNoTracking().AsSplitQuery()
            .Include(x => x.Beneficios).Include(x => x.VagaRequisitos)
            .Include(x => x.VagaAtividades).Include(x => x.VagaDiferencials)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (v == null) return null;
        return (new VagaForm
        {
            Id = v.Id, Titulo = v.Titulo, Descricao = v.Descricao, UnidadeId = v.UnidadeId, AreaId = v.AreaId,
            TurnoId = v.TurnoId, TipoContratoId = v.TipoContratoId, ModalidadeId = v.ModalidadeId, ProgramaId = v.ProgramaId,
            SalarioMin = v.SalarioMin, SalarioMax = v.SalarioMax, ExibirSalario = v.ExibirSalario,
            BeneficioIds = v.Beneficios.Select(b => b.Id).ToList(),
            Requisitos = v.VagaRequisitos.OrderBy(r => r.Id).Select(r => new RequisitoForm { Texto = r.Texto, Obrigatorio = r.Obrigatorio }).ToList(),
            Atividades = v.VagaAtividades.OrderBy(a => a.Id).Select(a => a.Texto).ToList(),
            Diferenciais = v.VagaDiferencials.OrderBy(d => d.Id).Select(d => d.Texto).ToList(),
        }, v.Status);
    }

    public static Dictionary<string, string> Validar(VagaForm f)
    {
        var e = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(f.Titulo)) e[nameof(f.Titulo)] = "Informe o título da vaga.";
        else if (f.Titulo.Trim().Length > 150) e[nameof(f.Titulo)] = "Use até 150 caracteres no título.";
        if (string.IsNullOrWhiteSpace(f.Descricao)) e[nameof(f.Descricao)] = "Descreva a vaga.";
        if (f.UnidadeId <= 0) e[nameof(f.UnidadeId)] = "Escolha a unidade.";
        if (f.AreaId <= 0) e[nameof(f.AreaId)] = "Escolha a área.";
        if (f.TurnoId <= 0) e[nameof(f.TurnoId)] = "Escolha o turno.";
        if (f.TipoContratoId <= 0) e[nameof(f.TipoContratoId)] = "Escolha o tipo de contrato.";
        if (f.ModalidadeId <= 0) e[nameof(f.ModalidadeId)] = "Escolha a modalidade.";
        var sal = RegrasSalario.Validar(f.SalarioMin, f.SalarioMax, f.ExibirSalario);
        if (sal != null) e[nameof(f.SalarioMin)] = sal;
        if (f.Requisitos.Any(r => r.Texto.Trim().Length > 300) || f.Atividades.Any(a => a.Trim().Length > 300)
            || f.Diferenciais.Any(d => d.Trim().Length > 300))
            e["Listas"] = "Cada item das listas aceita até 300 caracteres.";
        return e;
    }

    /// <summary>Cria (Id nulo) ou atualiza. Vaga encerrada abre bloqueada. O slug só acompanha título e unidade enquanto é rascunho (RN01).</summary>
    public async Task<(Resultado Resultado, int? Id, Dictionary<string, string> Erros)> SalvarAsync(
        VagaForm f, int atorId, CancellationToken ct = default)
    {
        var erros = Validar(f);
        if (erros.Count > 0) return (Resultado.Falha("Corrija os campos indicados."), null, erros);

        var cidade = await _db.Unidades.Where(u => u.Id == f.UnidadeId).Select(u => u.Cidade).SingleOrDefaultAsync(ct);
        if (cidade == null)
        {
            erros[nameof(f.UnidadeId)] = "Escolha uma unidade válida.";
            return (Resultado.Falha("Corrija os campos indicados."), null, erros);
        }

        Vaga v;
        if (f.Id is null)
        {
            v = new Vaga { Status = StatusVaga.Rascunho, Slug = await NovoSlugAsync(f.Titulo, cidade, null, ct) };
            _db.Vagas.Add(v);
            v.VagaStatusHists.Add(new VagaStatusHist { Status = StatusVaga.Rascunho, UsuarioRhId = atorId });
        }
        else
        {
            v = await _db.Vagas.AsSplitQuery()
                .Include(x => x.Beneficios).Include(x => x.VagaRequisitos)
                .Include(x => x.VagaAtividades).Include(x => x.VagaDiferencials)
                .SingleOrDefaultAsync(x => x.Id == f.Id, ct) ?? throw new InvalidOperationException("Vaga não encontrada.");
            if (v.Status == StatusVaga.Encerrada)
                return (Resultado.Falha("Vaga encerrada não pode ser editada. Duplique a vaga para abrir uma nova."), v.Id, erros);
            if (v.Status == StatusVaga.Rascunho && (v.Titulo != f.Titulo.Trim() || v.UnidadeId != f.UnidadeId))
                v.Slug = await NovoSlugAsync(f.Titulo, cidade, v.Id, ct);
        }

        v.Titulo = f.Titulo.Trim();
        v.Descricao = f.Descricao.Trim();
        v.UnidadeId = f.UnidadeId; v.AreaId = f.AreaId; v.TurnoId = f.TurnoId;
        v.TipoContratoId = f.TipoContratoId; v.ModalidadeId = f.ModalidadeId; v.ProgramaId = f.ProgramaId;
        v.SalarioMin = f.SalarioMin; v.SalarioMax = f.SalarioMax; v.ExibirSalario = f.ExibirSalario;

        // Listas: só mexe no que mudou (comparação como conjunto, pelo texto).
        SincronizarTextos(v.VagaRequisitos, f.Requisitos.Select(r => (r.Texto.Trim(), r.Obrigatorio)).Where(r => r.Item1.Length > 0),
            (t, o) => new VagaRequisito { Texto = t, Obrigatorio = o }, r => r.Texto, (r, o) => r.Obrigatorio = o);
        SincronizarTextos(v.VagaAtividades, f.Atividades.Select(t => (t.Trim(), true)).Where(r => r.Item1.Length > 0),
            (t, _) => new VagaAtividade { Texto = t }, a => a.Texto, null);
        SincronizarTextos(v.VagaDiferencials, f.Diferenciais.Select(t => (t.Trim(), true)).Where(r => r.Item1.Length > 0),
            (t, _) => new VagaDiferencial { Texto = t }, d => d.Texto, null);

        var desejados = f.BeneficioIds.Distinct().ToHashSet();
        foreach (var b in v.Beneficios.Where(b => !desejados.Contains(b.Id)).ToList()) v.Beneficios.Remove(b);
        var jaTem = v.Beneficios.Select(b => b.Id).ToHashSet();
        var novos = desejados.Where(i => !jaTem.Contains(i)).ToList();
        if (novos.Count > 0)
            foreach (var b in await _db.Beneficios.Where(b => novos.Contains(b.Id)).ToListAsync(ct)) v.Beneficios.Add(b);

        await _db.SaveChangesAsync(ct);   // o interceptador grava o log; sem mudança, nada é gravado
        return (Resultado.Sucesso(), v.Id, erros);
    }

    public async Task<Resultado> MudarStatusAsync(int id, string novo, int atorId, CancellationToken ct = default)
    {
        var v = await _db.Vagas.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (v == null) return Resultado.Falha("Vaga não encontrada.");
        if (!StatusVaga.PodeTransitar(v.Status, novo))
            return Resultado.Falha($"Não é possível passar de {v.Status} para {novo}.");
        v.Status = novo;
        if (novo == StatusVaga.Publicada && v.PublicadaEm == null) v.PublicadaEm = DateTime.UtcNow;
        _db.VagaStatusHists.Add(new VagaStatusHist { VagaId = id, Status = novo, UsuarioRhId = atorId });
        await _db.SaveChangesAsync(ct);
        return Resultado.Sucesso();
    }

    /// <summary>Cria um rascunho com os mesmos dados e slug novo e único.</summary>
    public async Task<(Resultado, int?)> DuplicarAsync(int id, int atorId, CancellationToken ct = default)
    {
        var o = await ObterAsync(id, ct);
        if (o == null) return (Resultado.Falha("Vaga não encontrada."), null);
        var copia = o.Value.Form;
        copia.Id = null;
        var r = await SalvarAsync(copia, atorId, ct);
        return (r.Resultado, r.Id);
    }

    private async Task<string> NovoSlugAsync(string titulo, string cidade, int? ignorarId, CancellationToken ct)
    {
        var baseSlug = SlugVaga.Base(titulo.Trim(), cidade);
        var usados = await _db.Vagas.Where(v => v.Slug.StartsWith(baseSlug) && v.Id != ignorarId)
            .Select(v => v.Slug).ToListAsync(ct);
        return SlugVaga.Unico(baseSlug, usados.ToHashSet(StringComparer.OrdinalIgnoreCase).Contains);
    }

    private void SincronizarTextos<T>(
        ICollection<T> atuais, IEnumerable<(string Texto, bool Obrig)> desejados,
        Func<string, bool, T> criar, Func<T, string> texto, Action<T, bool>? atualizarObrig) where T : class
    {
        var lista = desejados.GroupBy(d => d.Texto, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).ToList();
        var chaves = lista.Select(d => d.Texto).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        foreach (var a in atuais.Where(a => !chaves.Contains(texto(a))).ToList())
        {
            atuais.Remove(a);
            _db.Remove(a);
        }
        foreach (var (t, o) in lista)
        {
            var existente = atuais.FirstOrDefault(a => string.Equals(texto(a), t, StringComparison.CurrentCultureIgnoreCase));
            if (existente == null) atuais.Add(criar(t, o));
            else atualizarObrig?.Invoke(existente, o);
        }
    }
}
