namespace Carreiras.Dominio.Usuarios;

public enum SituacaoUsuario { Ativo, ConvitePendente, Desativado }

public static class RegrasUsuario
{
    public const string MotivoProprio = "Você não pode alterar a própria conta.";
    public const string MotivoUltimoAdmin =
        "É o único administrador ativo. Cadastre outro administrador antes de desativar este.";

    public static SituacaoUsuario Situacao(bool ativo, bool conviteAceito) =>
        !ativo ? SituacaoUsuario.Desativado : conviteAceito ? SituacaoUsuario.Ativo : SituacaoUsuario.ConvitePendente;

    // RN17
    public static bool EhProprio(int idAtor, int idAlvo) => idAtor == idAlvo;

    /// <summary>RN16: administrador "que consegue entrar" = ativo, convite aceito, perfil ADMINISTRADOR.</summary>
    public static bool PerderiaUltimoAdministrador(
        string perfilAtualAlvo, bool alvoConsegueEntrar, int outrosAdministradoresQueEntram) =>
        perfilAtualAlvo == Perfis.Administrador && alvoConsegueEntrar && outrosAdministradoresQueEntram == 0;
}
