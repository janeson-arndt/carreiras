using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Candidatura")]
[Index("VagaId", "Status", Name = "IX_Candidatura_Vaga")]
public partial class Candidatura
{
    [Key]
    public int Id { get; set; }

    public int CandidatoId { get; set; }

    public int? VagaId { get; set; }

    public int CurriculoId { get; set; }

    [Column(TypeName = "decimal(10, 2)")]
    public decimal? PretensaoSalarial { get; set; }

    [StringLength(15)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Precision(0)]
    public DateTime CriadaEm { get; set; }

    [ForeignKey("CandidatoId")]
    [InverseProperty("Candidaturas")]
    public virtual Candidato Candidato { get; set; } = null!;

    [InverseProperty("Candidatura")]
    public virtual ICollection<CandidaturaStatusHist> CandidaturaStatusHists { get; set; } = new List<CandidaturaStatusHist>();

    [ForeignKey("CurriculoId")]
    [InverseProperty("Candidaturas")]
    public virtual Curriculo Curriculo { get; set; } = null!;

    [ForeignKey("VagaId")]
    [InverseProperty("Candidaturas")]
    public virtual Vaga? Vaga { get; set; }
}
