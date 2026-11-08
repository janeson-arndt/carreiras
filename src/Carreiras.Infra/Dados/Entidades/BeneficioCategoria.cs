using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Index("Nome", Name = "UQ_BeneficioCategoria_Nome", IsUnique = true)]
public partial class BeneficioCategoria
{
    [Key]
    public int Id { get; set; }

    [StringLength(60)]
    public string Nome { get; set; } = null!;

    public int Ordem { get; set; }

    [InverseProperty("Categoria")]
    public virtual ICollection<Beneficio> Beneficios { get; set; } = new List<Beneficio>();
}
