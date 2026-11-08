using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("VagaStatusHist")]
public partial class VagaStatusHist
{
    [Key]
    public int Id { get; set; }

    public int VagaId { get; set; }

    [StringLength(15)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Precision(0)]
    public DateTime AlteradoEm { get; set; }

    public int? UsuarioRhId { get; set; }

    [ForeignKey("UsuarioRhId")]
    [InverseProperty("VagaStatusHists")]
    public virtual UsuarioRh? UsuarioRh { get; set; }

    [ForeignKey("VagaId")]
    [InverseProperty("VagaStatusHists")]
    public virtual Vaga Vaga { get; set; } = null!;
}
