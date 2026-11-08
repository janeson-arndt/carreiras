using System.Text.RegularExpressions;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Usuarios;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Cadastros;

public sealed class BeneficioForm
{
    public int? Id { get; set; }
    public int CategoriaId { get; set; }
    public string Nome { get; set; } = "";
    public string? DescricaoCurta { get; set; }
    public string? Icone { get; set; }
    public int Ordem { get; set; }
}

public sealed class ProgramaForm
{
    public int? Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Descricao { get; set; }
    public string? PublicoAlvo { get; set; }
}

/// <summary>
/// Cadastros da vitrine: benefícios e programas. Nunca exclui: só ativa e desativa (a vaga pode usar o item).
/// As escritas passam pelo interceptador, então cada mudança de campo vai para LogAuditoria.
/// </summary>
public sealed class CadastroServico
{
    private static readonly Regex IconeValido = new("^bi-[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private readonly AppDbContext _db;
    public CadastroServico(AppDbContext db) => _db = db;

    // ---------------------------------------------------------------- benefícios

    public Task<List<BeneficioCategoria>> ListarBeneficiosAsync(CancellationToken ct = default) =>
        _db.BeneficioCategoria.AsNoTracking().Include(c => c.Beneficios).OrderBy(c => c.Ordem).ToListAsync(ct);

    public Task<List<BeneficioCategoria>> CategoriasAsync(CancellationToken ct = default) =>
        _db.BeneficioCategoria.AsNoTracking().OrderBy(c => c.Ordem).ToListAsync(ct);

    public async Task<BeneficioForm?> ObterBeneficioAsync(int id, CancellationToken ct = default)
    {
        var b = await _db.Beneficios.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return b == null ? null : new BeneficioForm
        {
            Id = b.Id, CategoriaId = b.CategoriaId, Nome = b.Nome, DescricaoCurta = b.DescricaoCurta, Icone = b.Icone, Ordem = b.Ordem,
        };
    }

    public static Dictionary<string, string> Validar(BeneficioForm f)
    {
        var e = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(f.Nome)) e[nameof(f.Nome)] = "Informe o nome do benefício.";
        else if (f.Nome.Trim().Length > 80) e[nameof(f.Nome)] = "Use até 80 caracteres no nome.";
        if (f.CategoriaId <= 0) e[nameof(f.CategoriaId)] = "Escolha a categoria.";
        if ((f.DescricaoCurta?.Trim().Length ?? 0) > 160) e[nameof(f.DescricaoCurta)] = "Use até 160 caracteres na descrição.";
        if (!string.IsNullOrWhiteSpace(f.Icone) && !IconeValido.IsMatch(f.Icone.Trim()))
            e[nameof(f.Icone)] = "Use o nome de um ícone do Bootstrap Icons, como bi-credit-card.";
        if (f.Ordem < 0 || f.Ordem > 9999) e[nameof(f.Ordem)] = "Informe uma ordem entre 0 e 9999.";
        return e;
    }

    public async Task<(Resultado, int?, Dictionary<string, string>)> SalvarBeneficioAsync(BeneficioForm f, CancellationToken ct = default)
    {
        var erros = Validar(f);
        var nome = (f.Nome ?? "").Trim();
        if (erros.Count == 0 && f.CategoriaId > 0 && !await _db.BeneficioCategoria.AnyAsync(c => c.Id == f.CategoriaId, ct))
            erros[nameof(f.CategoriaId)] = "Escolha uma categoria válida.";
        if (erros.Count == 0 && await _db.Beneficios.AnyAsync(b => b.Nome == nome && b.Id != f.Id, ct))
            erros[nameof(f.Nome)] = "Já existe um benefício com este nome.";
        if (erros.Count > 0) return (Resultado.Falha("Corrija os campos indicados."), f.Id, erros);

        var b = f.Id is null ? _db.Beneficios.Add(new Beneficio { Ativo = true }).Entity
            : await _db.Beneficios.SingleOrDefaultAsync(x => x.Id == f.Id, ct) ?? throw new InvalidOperationException("Benefício não encontrado.");
        b.Nome = nome;
        b.CategoriaId = f.CategoriaId;
        b.DescricaoCurta = Vazio(f.DescricaoCurta);
        b.Icone = Vazio(f.Icone);
        b.Ordem = f.Ordem;
        await _db.SaveChangesAsync(ct);
        return (Resultado.Sucesso(), b.Id, erros);
    }

    public async Task<Resultado> AlternarBeneficioAsync(int id, bool ativo, CancellationToken ct = default)
    {
        var b = await _db.Beneficios.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (b == null) return Resultado.Falha("Benefício não encontrado.");
        b.Ativo = ativo;
        await _db.SaveChangesAsync(ct);
        return Resultado.Sucesso();
    }

    // ---------------------------------------------------------------- programas

    public Task<List<Programa>> ListarProgramasAsync(CancellationToken ct = default) =>
        _db.Programas.AsNoTracking().OrderByDescending(p => p.Ativo).ThenBy(p => p.Nome).ToListAsync(ct);

    public async Task<ProgramaForm?> ObterProgramaAsync(int id, CancellationToken ct = default)
    {
        var p = await _db.Programas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return p == null ? null : new ProgramaForm { Id = p.Id, Nome = p.Nome, Descricao = p.Descricao, PublicoAlvo = p.PublicoAlvo };
    }

    public static Dictionary<string, string> Validar(ProgramaForm f)
    {
        var e = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(f.Nome)) e[nameof(f.Nome)] = "Informe o nome do programa.";
        else if (f.Nome.Trim().Length > 100) e[nameof(f.Nome)] = "Use até 100 caracteres no nome.";
        if ((f.Descricao?.Trim().Length ?? 0) > 600) e[nameof(f.Descricao)] = "Use até 600 caracteres na descrição.";
        if ((f.PublicoAlvo?.Trim().Length ?? 0) > 200) e[nameof(f.PublicoAlvo)] = "Use até 200 caracteres no público-alvo.";
        return e;
    }

    public async Task<(Resultado, int?, Dictionary<string, string>)> SalvarProgramaAsync(ProgramaForm f, CancellationToken ct = default)
    {
        var erros = Validar(f);
        var nome = (f.Nome ?? "").Trim();
        if (erros.Count == 0 && await _db.Programas.AnyAsync(p => p.Nome == nome && p.Id != f.Id, ct))
            erros[nameof(f.Nome)] = "Já existe um programa com este nome.";
        if (erros.Count > 0) return (Resultado.Falha("Corrija os campos indicados."), f.Id, erros);

        var p = f.Id is null ? _db.Programas.Add(new Programa { Ativo = true }).Entity
            : await _db.Programas.SingleOrDefaultAsync(x => x.Id == f.Id, ct) ?? throw new InvalidOperationException("Programa não encontrado.");
        p.Nome = nome;
        p.Descricao = Vazio(f.Descricao);
        p.PublicoAlvo = Vazio(f.PublicoAlvo);
        await _db.SaveChangesAsync(ct);
        return (Resultado.Sucesso(), p.Id, erros);
    }

    public async Task<Resultado> AlternarProgramaAsync(int id, bool ativo, CancellationToken ct = default)
    {
        var p = await _db.Programas.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p == null) return Resultado.Falha("Programa não encontrado.");
        p.Ativo = ativo;
        await _db.SaveChangesAsync(ct);
        return Resultado.Sucesso();
    }

    private static string? Vazio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
