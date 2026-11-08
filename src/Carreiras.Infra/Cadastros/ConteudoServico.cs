using System.Text.RegularExpressions;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Usuarios;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Cadastros;

public sealed class ConteudoForm
{
    public int? Id { get; set; }
    public string Secao { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Texto { get; set; } = "";
    public int Ordem { get; set; }
    public bool Publicado { get; set; } = true;
    public bool Ficticio { get; set; }
}

/// <summary>
/// Textos institucionais da vitrine. A seção (chave) não muda depois de criada, porque o portal público
/// a usa para achar o texto. Não exclui: só despublica. Escritas passam pelo interceptador (auditoria).
/// </summary>
public sealed class ConteudoServico
{
    public const int TituloMax = 120;
    private static readonly Regex SecaoValida = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private readonly AppDbContext _db;
    public ConteudoServico(AppDbContext db) => _db = db;

    public Task<List<ConteudoInstitucional>> ListarAsync(CancellationToken ct = default) =>
        _db.ConteudoInstitucionals.AsNoTracking().OrderBy(c => c.Ordem).ThenBy(c => c.Titulo).ToListAsync(ct);

    public async Task<ConteudoForm?> ObterAsync(int id, CancellationToken ct = default)
    {
        var c = await _db.ConteudoInstitucionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return c == null ? null : new ConteudoForm
        {
            Id = c.Id, Secao = c.Secao, Titulo = c.Titulo, Texto = c.Texto, Ordem = c.Ordem, Publicado = c.Publicado, Ficticio = c.Ficticio,
        };
    }

    public static Dictionary<string, string> Validar(ConteudoForm f)
    {
        var e = new Dictionary<string, string>();
        var secao = (f.Secao ?? "").Trim();
        if (f.Id is null)
        {
            if (secao.Length == 0) e[nameof(f.Secao)] = "Informe a seção.";
            else if (secao.Length > 40) e[nameof(f.Secao)] = "Use até 40 caracteres na seção.";
            else if (!SecaoValida.IsMatch(secao)) e[nameof(f.Secao)] = "Use só letras minúsculas sem acento, números e hífen, como quem-somos.";
        }
        if (string.IsNullOrWhiteSpace(f.Titulo)) e[nameof(f.Titulo)] = "Informe o título.";
        else if (f.Titulo.Trim().Length > TituloMax) e[nameof(f.Titulo)] = $"Use até {TituloMax} caracteres no título.";
        if (string.IsNullOrWhiteSpace(f.Texto)) e[nameof(f.Texto)] = "Escreva o texto.";
        if (f.Ordem < 0 || f.Ordem > 9999) e[nameof(f.Ordem)] = "Informe uma ordem entre 0 e 9999.";
        return e;
    }

    public async Task<(Resultado, int?, Dictionary<string, string>)> SalvarAsync(ConteudoForm f, CancellationToken ct = default)
    {
        var erros = Validar(f);
        var secao = (f.Secao ?? "").Trim();
        if (erros.Count == 0 && f.Id is null && await _db.ConteudoInstitucionals.AnyAsync(c => c.Secao == secao, ct))
            erros[nameof(f.Secao)] = "Já existe um conteúdo nesta seção.";
        if (erros.Count > 0) return (Resultado.Falha("Corrija os campos indicados."), f.Id, erros);

        ConteudoInstitucional c;
        if (f.Id is null) c = _db.ConteudoInstitucionals.Add(new ConteudoInstitucional { Secao = secao }).Entity;
        else c = await _db.ConteudoInstitucionals.SingleOrDefaultAsync(x => x.Id == f.Id, ct)
                 ?? throw new InvalidOperationException("Conteúdo não encontrado.");   // a seção de um existente nunca muda

        c.Titulo = f.Titulo.Trim();
        c.Texto = f.Texto.Replace("\r\n", "\n").Trim();   // CRLF do navegador não pode gerar log à toa
        c.Ordem = f.Ordem;
        c.Publicado = f.Publicado;
        c.Ficticio = f.Ficticio;
        await _db.SaveChangesAsync(ct);
        return (Resultado.Sucesso(), c.Id, erros);
    }

    public async Task<Resultado> AlternarPublicadoAsync(int id, bool publicado, CancellationToken ct = default)
    {
        var c = await _db.ConteudoInstitucionals.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return Resultado.Falha("Conteúdo não encontrado.");
        c.Publicado = publicado;
        await _db.SaveChangesAsync(ct);
        return Resultado.Sucesso();
    }
}
