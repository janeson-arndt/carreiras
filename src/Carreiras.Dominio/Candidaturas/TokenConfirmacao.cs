using System.Security.Cryptography;
using System.Text;

namespace Carreiras.Dominio.Candidaturas;

/// <summary>Termo de privacidade do candidato. A versão é gravada em cada aceite (RN08): mude o texto, mude a versão.</summary>
public static class TermoPrivacidade
{
    public const string VersaoAtual = "1.0";
}

/// <summary>
/// RN07: o link de confirmação carrega um token aleatório forte (256 bits). O banco guarda só o hash SHA-256:
/// quem lê o banco não consegue montar o link.
/// </summary>
public static class TokenConfirmacao
{
    public static (string Token, string Hash) Gerar()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
