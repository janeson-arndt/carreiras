using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Carreiras.Infra.Candidaturas;

public sealed class CurriculosOpcoes
{
    /// <summary>Pasta dos currículos. Fica FORA do wwwroot: nenhum arquivo daqui é servido pelo site.</summary>
    public string Pasta { get; set; } = "storage/curriculos";
}

/// <summary>
/// Guarda os currículos em disco com nome gerado (guid + extensão); o nome enviado pelo candidato nunca vira
/// caminho. O banco guarda só esse nome gerado em Curriculo.Caminho.
/// </summary>
public sealed partial class ArmazenamentoCurriculos
{
    private readonly string _pasta;

    public ArmazenamentoCurriculos(IOptions<CurriculosOpcoes> op) => _pasta = Path.GetFullPath(op.Value.Pasta);

    public string Pasta => _pasta;

    public async Task<string> SalvarAsync(Stream conteudo, string extensao, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_pasta);
        var nome = Guid.NewGuid().ToString("N") + extensao.ToLowerInvariant();
        if (!NomeValido().IsMatch(nome)) throw new InvalidOperationException("Extensão inválida.");
        conteudo.Position = 0;
        await using var f = new FileStream(Path.Combine(_pasta, nome), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await conteudo.CopyToAsync(f, ct);
        return nome;
    }

    /// <summary>Abre o arquivo para leitura (uso do RH autenticado, Entrega 06). Nome fora do padrão = nunca abre.</summary>
    public Stream? Abrir(string nome)
    {
        if (!NomeValido().IsMatch(nome)) return null;
        var caminho = Path.Combine(_pasta, nome);
        return File.Exists(caminho) ? new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public bool Existe(string nome) => NomeValido().IsMatch(nome) && File.Exists(Path.Combine(_pasta, nome));

    public void Apagar(string nome)
    {
        if (!NomeValido().IsMatch(nome)) return;
        var caminho = Path.Combine(_pasta, nome);
        if (File.Exists(caminho)) File.Delete(caminho);
    }

    [GeneratedRegex(@"^[0-9a-f]{32}\.(pdf|docx)$")] private static partial Regex NomeValido();
}
