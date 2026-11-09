using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Carreiras.Dominio.Candidaturas;

/// <summary>Dados que o candidato informa no formulário (o currículo é validado à parte).</summary>
public sealed record DadosCandidatura(
    string Nome, string Email, string Telefone, string Cidade, string Uf, decimal? PretensaoSalarial, bool AceitouTermo);

/// <summary>Regras de preenchimento da candidatura (Entrega 04). As chaves do dicionário são os nomes dos campos.</summary>
public static partial class ValidacaoCandidatura
{
    private static readonly HashSet<string> Ufs = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG","PA","PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO",
    };

    public static string NormalizarEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public static string SomenteDigitos(string? s) => NaoDigito().Replace(s ?? "", "");

    public static bool EmailValido(string email)
    {
        if (email.Length is 0 or > 256 || email.Any(char.IsWhiteSpace)) return false;
        try { var a = new MailAddress(email); return a.Address == email && email.Contains('.', StringComparison.Ordinal) && !email.EndsWith('.'); }
        catch (FormatException) { return false; }
    }

    public static Dictionary<string, string> Validar(DadosCandidatura d)
    {
        var e = new Dictionary<string, string>();
        var nome = (d.Nome ?? "").Trim();
        if (nome.Length < 3) e["Nome"] = "Informe seu nome completo.";
        else if (nome.Length > 120) e["Nome"] = "Use até 120 caracteres no nome.";

        if (!EmailValido(NormalizarEmail(d.Email))) e["Email"] = "Informe um e-mail válido, como nome@exemplo.com.";

        var tel = (d.Telefone ?? "").Trim();
        var digitos = SomenteDigitos(tel);
        if (digitos.Length is < 10 or > 13) e["Telefone"] = "Informe o telefone com DDD.";
        else if (tel.Length > 20) e["Telefone"] = "Use até 20 caracteres no telefone.";

        var cidade = (d.Cidade ?? "").Trim();
        if (cidade.Length < 2) e["Cidade"] = "Informe a cidade.";
        else if (cidade.Length > 80) e["Cidade"] = "Use até 80 caracteres na cidade.";

        if (!Ufs.Contains((d.Uf ?? "").Trim())) e["Uf"] = "Use a sigla do estado, com 2 letras.";

        if (d.PretensaoSalarial is < 0 or > 99_999_999m) e["PretensaoSalarial"] = "Informe um valor maior ou igual a zero.";

        if (!d.AceitouTermo) e["Aceite"] = "Para enviar, aceite o termo de privacidade.";
        return e;
    }

    [GeneratedRegex(@"\D")] private static partial Regex NaoDigito();
}
