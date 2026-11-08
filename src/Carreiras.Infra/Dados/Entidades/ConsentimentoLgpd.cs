using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("ConsentimentoLgpd")]
public partial class ConsentimentoLgpd
{
    [Key]
    public int Id { get; set; }

    public int CandidatoId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string VersaoTermo { get; set; } = null!;

    [Precision(0)]
    public DateTime AceitoEm { get; set; }

    [StringLength(45)]
    [Unicode(false)]
    public string? Ip { get; set; }

    [ForeignKey("CandidatoId")]
    [InverseProperty("ConsentimentoLgpds")]
    public virtual Candidato Candidato { get; set; } = null!;
}
