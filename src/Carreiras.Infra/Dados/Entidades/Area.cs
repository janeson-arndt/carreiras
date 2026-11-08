using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Area")]
[Index("Nome", Name = "UQ_Area_Nome", IsUnique = true)]
public partial class Area
{
    [Key]
    public int Id { get; set; }

    [StringLength(80)]
    public string Nome { get; set; } = null!;

    public bool Ativo { get; set; }

    [InverseProperty("Area")]
    public virtual ICollection<Vaga> Vagas { get; set; } = new List<Vaga>();
}
