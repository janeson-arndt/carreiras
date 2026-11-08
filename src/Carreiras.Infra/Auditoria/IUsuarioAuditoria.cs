namespace Carreiras.Infra.Auditoria;

/// <summary>Quem está agindo (UsuarioRh.Id). Preenchido pela camada web a cada requisição.</summary>
public interface IUsuarioAuditoria
{
    int? UsuarioRhId { get; }
}

public sealed class UsuarioAuditoriaFixo : IUsuarioAuditoria
{
    public int? UsuarioRhId { get; set; }
}
