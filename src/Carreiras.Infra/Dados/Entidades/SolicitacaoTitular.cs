using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("SolicitacaoTitular")]
public partial class SolicitacaoTitular
{
    [Key]
    public int Id { get; set; }

    public int CandidatoId { get; set; }

    [StringLength(10)]
    [Unicode(false)]
    public string Tipo { get; set; } = null!;

    [StringLength(12)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Precision(0)]
    public DateTime AbertaEm { get; set; }

    [Precision(0)]
    public DateTime? AtendidaEm { get; set; }

    [ForeignKey("CandidatoId")]
    [InverseProperty("SolicitacaoTitulars")]
    public virtual Candidato Candidato { get; set; } = null!;
}
