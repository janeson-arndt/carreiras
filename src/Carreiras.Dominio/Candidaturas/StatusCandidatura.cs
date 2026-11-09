namespace Carreiras.Dominio.Candidaturas;

/// <summary>Situações de uma candidatura (CK_Candidatura_Status). A candidatura nasce RECEBIDA; o RH muda livremente depois.</summary>
public static class StatusCandidatura
{
    public const string Recebida = "RECEBIDA";
    public const string EmAnalise = "EM_ANALISE";
    public const string Entrevista = "ENTREVISTA";
    public const string Aprovada = "APROVADA";
    public const string Reprovada = "REPROVADA";

    public static readonly IReadOnlyList<string> Todos = new[] { Recebida, EmAnalise, Entrevista, Aprovada, Reprovada };

    public static bool Valido(string? s) => s != null && Todos.Contains(s);

    public static string Rotulo(string s) => s switch
    {
        Recebida => "Recebida",
        EmAnalise => "Em análise",
        Entrevista => "Entrevista",
        Aprovada => "Aprovada",
        Reprovada => "Reprovada",
        _ => s,
    };
}
