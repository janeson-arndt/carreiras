namespace Carreiras.Dominio.Usuarios;

public static class Perfis
{
    public const string Administrador = "ADMINISTRADOR";
    public const string Analista = "ANALISTA";
    public const string Leitura = "LEITURA";

    public static readonly string[] Todos = { Administrador, Analista, Leitura };
}

public static class Politicas
{
    public const string ConsultarAdmin = "ConsultarAdmin";   // qualquer perfil do RH
    public const string GerenciarVagas = "GerenciarVagas";   // ADMINISTRADOR, ANALISTA
    public const string SomenteAdministrador = "SomenteAdministrador";
}
