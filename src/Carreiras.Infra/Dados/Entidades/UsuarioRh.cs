using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("UsuarioRh")]
[Index("Email", Name = "UQ_UsuarioRh_Email", IsUnique = true)]
public partial class UsuarioRh
{
    [Key]
    public int Id { get; set; }

    [StringLength(120)]
    public string Nome { get; set; } = null!;

    [StringLength(256)]
    public string Email { get; set; } = null!;

    [StringLength(400)]
    public string? SenhaHash { get; set; }

    public int PerfilId { get; set; }

    public bool Ativo { get; set; }

    [Precision(0)]
    public DateTime CriadoEm { get; set; }

    [Precision(0)]
    public DateTime? UltimoAcessoEm { get; set; }

    public int? CriadoPorUsuarioRhId { get; set; }

    public string? IdentityUserId { get; set; }

    [InverseProperty("UsuarioRh")]
    public virtual ICollection<CandidaturaStatusHist> CandidaturaStatusHists { get; set; } = new List<CandidaturaStatusHist>();

    [ForeignKey("CriadoPorUsuarioRhId")]
    [InverseProperty("InverseCriadoPorUsuarioRh")]
    public virtual UsuarioRh? CriadoPorUsuarioRh { get; set; }

    [InverseProperty("CriadoPorUsuarioRh")]
    public virtual ICollection<UsuarioRh> InverseCriadoPorUsuarioRh { get; set; } = new List<UsuarioRh>();

    [InverseProperty("UsuarioRh")]
    public virtual ICollection<LogAuditoria> LogAuditoria { get; set; } = new List<LogAuditoria>();

    [ForeignKey("PerfilId")]
    [InverseProperty("UsuarioRhs")]
    public virtual PerfilRh Perfil { get; set; } = null!;

    [InverseProperty("UsuarioRh")]
    public virtual ICollection<VagaStatusHist> VagaStatusHists { get; set; } = new List<VagaStatusHist>();
}
