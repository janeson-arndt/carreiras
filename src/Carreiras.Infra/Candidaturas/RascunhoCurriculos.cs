using System.Text.RegularExpressions;

namespace Carreiras.Infra.Candidaturas;

/// <summary>
/// Guarda por pouco tempo o currículo de um envio que voltou com críticas (outro campo errado), para a pessoa não
/// precisar escolher o arquivo de novo. Fica numa subpasta da pasta de currículos, fora do wwwroot, com nome gerado
/// (guid + extensão). Rascunho que ninguém usou é apagado depois de <see cref="Validade"/>.
/// </summary>
public sealed partial class RascunhoCurriculos
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(2);

    private readonly string _pasta;

    public RascunhoCurriculos(ArmazenamentoCurriculos arquivos) => _pasta = Path.Combine(arquivos.Pasta, "rascunho");

    public string Pasta => _pasta;

    public async Task<string> SalvarAsync(Stream conteudo, string extensao, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_pasta);
        LimparVencidos();
        var nome = Guid.NewGuid().ToString("N") + extensao.ToLowerInvariant();
        if (!NomeValido().IsMatch(nome)) throw new InvalidOperationException("Extensão inválida.");
        conteudo.Position = 0;
        await using var f = new FileStream(Path.Combine(_pasta, nome), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await conteudo.CopyToAsync(f, ct);
        return nome;
    }

    /// <summary>Abre o rascunho para leitura. Nome fora do padrão, ou arquivo já vencido/apagado = null.</summary>
    public Stream? Abrir(string nome)
    {
        if (!NomeValido().IsMatch(nome)) return null;
        var caminho = Path.Combine(_pasta, nome);
        if (!File.Exists(caminho) || DateTime.UtcNow - File.GetCreationTimeUtc(caminho) > Validade) return null;
        try { return new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (IOException) { return null; }
    }

    public void Apagar(string nome)
    {
        if (!NomeValido().IsMatch(nome)) return;
        var caminho = Path.Combine(_pasta, nome);
        try { if (File.Exists(caminho)) File.Delete(caminho); }
        catch (IOException) { /* em uso: o próximo LimparVencidos apaga */ }
    }

    /// <summary>Apaga os rascunhos com mais de <see cref="Validade"/>. Chamado a cada novo rascunho.</summary>
    public void LimparVencidos()
    {
        if (!Directory.Exists(_pasta)) return;
        foreach (var f in Directory.EnumerateFiles(_pasta))
        {
            try { if (DateTime.UtcNow - File.GetCreationTimeUtc(f) > Validade) File.Delete(f); }
            catch (IOException) { }
        }
    }

    [GeneratedRegex(@"^[0-9a-f]{32}\.(pdf|docx)$")] private static partial Regex NomeValido();
}
