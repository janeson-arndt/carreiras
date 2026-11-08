namespace Carreiras.Dominio.Vagas;

public static class RegrasSalario
{
    // RN04: Max >= Min; para exibir, os dois valores sao obrigatorios.
    public static string? Validar(decimal? min, decimal? max, bool exibir)
    {
        if (min < 0 || max < 0) return "Informe valores maiores ou iguais a zero.";
        if (min.HasValue && max.HasValue && max < min) return "O salário máximo não pode ser menor que o mínimo.";
        if (exibir && (!min.HasValue || !max.HasValue)) return "Para mostrar a faixa salarial, informe o mínimo e o máximo.";
        return null;
    }
}
