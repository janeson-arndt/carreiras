using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Carreiras.Dominio.Vagas;

public static class SlugVaga
{
    // RN01: {titulo-normalizado}-{timbo|recife|canoas}; se existir, acrescenta -2, -3...
    public static string Normalizar(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in decomposto)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        var limpo = Regex.Replace(sb.ToString().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return limpo;
    }

    public static string Base(string titulo, string cidade)
    {
        var t = Normalizar(titulo);
        var c = Normalizar(cidade);
        return string.IsNullOrEmpty(c) ? t : $"{t}-{c}";
    }

    public static string Unico(string baseSlug, Func<string, bool> existe, int maxLen = 150)
    {
        if (baseSlug.Length > maxLen - 6) baseSlug = baseSlug[..(maxLen - 6)].TrimEnd('-');
        if (!existe(baseSlug)) return baseSlug;
        for (var n = 2; ; n++)
        {
            var candidato = $"{baseSlug}-{n}";
            if (!existe(candidato)) return candidato;
        }
    }
}
