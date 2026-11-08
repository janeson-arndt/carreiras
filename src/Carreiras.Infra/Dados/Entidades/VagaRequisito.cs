using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("VagaRequisito")]
public partial class VagaRequisito
{
    [Key]
    public int Id { get; set; }

    public int VagaId { get; set; }

    [StringLength(300)]
    public string Texto { get; set; } = null!;

    public bool Obrigatorio { get; set; }

    [ForeignKey("VagaId")]
    [InverseProperty("VagaRequisitos")]
    public virtual Vaga Vaga { get; set; } = null!;
}
