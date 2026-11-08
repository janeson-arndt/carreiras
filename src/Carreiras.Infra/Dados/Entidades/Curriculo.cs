using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Curriculo")]
public partial class Curriculo
{
    [Key]
    public int Id { get; set; }

    public int CandidatoId { get; set; }

    [StringLength(200)]
    public string NomeArquivo { get; set; } = null!;

    [StringLength(400)]
    public string Caminho { get; set; } = null!;

    public int TamanhoKb { get; set; }

    [Precision(0)]
    public DateTime EnviadoEm { get; set; }

    [Precision(0)]
    public DateTime? RemovidoEm { get; set; }

    [ForeignKey("CandidatoId")]
    [InverseProperty("Curriculos")]
    public virtual Candidato Candidato { get; set; } = null!;

    [InverseProperty("Curriculo")]
    public virtual ICollection<Candidatura> Candidaturas { get; set; } = new List<Candidatura>();
}
