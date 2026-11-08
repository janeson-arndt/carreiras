namespace Carreiras.Dominio.Vagas;

public static class StatusVaga
{
    public const string Rascunho = "RASCUNHO";
    public const string Publicada = "PUBLICADA";
    public const string Pausada = "PAUSADA";
    public const string Encerrada = "ENCERRADA";

    // RN02: RASCUNHO->PUBLICADA; PUBLICADA<->PAUSADA; PUBLICADA/PAUSADA->ENCERRADA. Encerrada nao reabre.
    private static readonly Dictionary<string, string[]> Permitidas = new()
    {
        [Rascunho] = new[] { Publicada },
        [Publicada] = new[] { Pausada, Encerrada },
        [Pausada] = new[] { Publicada, Encerrada },
        [Encerrada] = Array.Empty<string>(),
    };

    public static bool PodeTransitar(string de, string para) =>
        Permitidas.TryGetValue(de, out var destinos) && destinos.Contains(para);

    public static IReadOnlyList<string> Destinos(string de) =>
        Permitidas.TryGetValue(de, out var d) ? d : Array.Empty<string>();
}
