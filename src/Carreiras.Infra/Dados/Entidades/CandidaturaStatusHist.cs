using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("CandidaturaStatusHist")]
public partial class CandidaturaStatusHist
{
    [Key]
    public int Id { get; set; }

    public int CandidaturaId { get; set; }

    [StringLength(15)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Precision(0)]
    public DateTime AlteradoEm { get; set; }

    public int? UsuarioRhId { get; set; }

    [ForeignKey("CandidaturaId")]
    [InverseProperty("CandidaturaStatusHists")]
    public virtual Candidatura Candidatura { get; set; } = null!;

    [ForeignKey("UsuarioRhId")]
    [InverseProperty("CandidaturaStatusHists")]
    public virtual UsuarioRh? UsuarioRh { get; set; }
}
