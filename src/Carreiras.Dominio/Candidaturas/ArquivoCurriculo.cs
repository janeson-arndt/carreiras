using System.IO.Compression;
using System.Text;

namespace Carreiras.Dominio.Candidaturas;

public sealed record ResultadoArquivo(string? Erro, string? Extensao, int TamanhoKb)
{
    public bool Ok => Erro == null;
}

/// <summary>
/// RN10: currículo em PDF ou DOCX, até 5 MB, conferindo o tipo REAL do arquivo (o cabeçalho e, no DOCX, a estrutura do
/// pacote), e não só a extensão. Um executável renomeado para .pdf é recusado.
/// </summary>
public static class ArquivoCurriculo
{
    public const long TamanhoMaximo = 5L * 1024 * 1024;
    public const string Aceitos = "PDF ou DOCX";

    public static ResultadoArquivo Validar(string? nomeArquivo, Stream? conteudo)
    {
        if (conteudo == null || conteudo.Length == 0 || string.IsNullOrWhiteSpace(nomeArquivo))
            return Falha("Anexe seu currículo em PDF ou DOCX.");

        var ext = Path.GetExtension(nomeArquivo).ToLowerInvariant();
        if (ext is not (".pdf" or ".docx")) return Falha("Formato não aceito. Envie um arquivo PDF ou DOCX.");
        if (conteudo.Length > TamanhoMaximo) return Falha("O arquivo tem mais de 5 MB. Envie um arquivo menor.");

        var kb = (int)Math.Max(1, Math.Ceiling(conteudo.Length / 1024.0));
        conteudo.Position = 0;
        try
        {
            if (ext == ".pdf")
            {
                var buf = new byte[Math.Min(1024, (int)conteudo.Length)];
                conteudo.ReadExactly(buf);
                // o cabeçalho %PDF- pode vir até o byte 1024 (algumas ferramentas colocam lixo antes)
                if (Encoding.Latin1.GetString(buf).IndexOf("%PDF-", StringComparison.Ordinal) < 0)
                    return Falha("O arquivo não parece ser um PDF válido. Gere o PDF de novo e envie.");
            }
            else
            {
                var magico = new byte[4];
                conteudo.ReadExactly(magico);
                if (!(magico[0] == 'P' && magico[1] == 'K' && magico[2] == 3 && magico[3] == 4))
                    return Falha("O arquivo não parece ser um DOCX válido. Salve o documento como DOCX e envie.");
                conteudo.Position = 0;
                using var zip = new ZipArchive(conteudo, ZipArchiveMode.Read, leaveOpen: true);
                if (zip.Entries.Count > 5000) return Falha("O arquivo não parece ser um DOCX válido.");
                var nomes = zip.Entries.Select(x => x.FullName).ToHashSet(StringComparer.Ordinal);
                if (!nomes.Contains("[Content_Types].xml") || !nomes.Contains("word/document.xml"))
                    return Falha("O arquivo não parece ser um DOCX válido. Salve o documento como DOCX e envie.");
            }
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or IOException)
        {
            return Falha("Não foi possível ler o arquivo. Envie outro currículo em PDF ou DOCX.");
        }
        finally { conteudo.Position = 0; }

        return new ResultadoArquivo(null, ext, kb);
    }

    /// <summary>Nome do arquivo só para exibição ao RH: sem pasta, sem caracteres de controle e com no máximo 200 caracteres.</summary>
    public static string NomeParaExibicao(string? nomeArquivo)
    {
        var nome = Path.GetFileName((nomeArquivo ?? "").Replace('\\', '/'));
        nome = new string(nome.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (nome.Length == 0) nome = "curriculo";
        return nome.Length <= 200 ? nome : nome[..200];
    }

    private static ResultadoArquivo Falha(string erro) => new(erro, null, 0);
}
