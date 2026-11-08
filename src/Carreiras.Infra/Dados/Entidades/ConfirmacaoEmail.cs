using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("ConfirmacaoEmail")]
[Index("TokenHash", Name = "IX_ConfirmacaoEmail_Token")]
public partial class ConfirmacaoEmail
{
    [Key]
    public int Id { get; set; }

    public int CandidatoId { get; set; }

    [StringLength(128)]
    [Unicode(false)]
    public string TokenHash { get; set; } = null!;

    [StringLength(15)]
    [Unicode(false)]
    public string Finalidade { get; set; } = null!;

    [Precision(0)]
    public DateTime ExpiraEm { get; set; }

    [Precision(0)]
    public DateTime? UsadoEm { get; set; }

    [ForeignKey("CandidatoId")]
    [InverseProperty("ConfirmacaoEmails")]
    public virtual Candidato Candidato { get; set; } = null!;
}
