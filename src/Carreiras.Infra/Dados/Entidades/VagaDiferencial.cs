using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("VagaDiferencial")]
public partial class VagaDiferencial
{
    [Key]
    public int Id { get; set; }

    public int VagaId { get; set; }

    [StringLength(300)]
    public string Texto { get; set; } = null!;

    [ForeignKey("VagaId")]
    [InverseProperty("VagaDiferencials")]
    public virtual Vaga Vaga { get; set; } = null!;
}
