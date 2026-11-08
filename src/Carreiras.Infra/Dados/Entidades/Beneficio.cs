using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Beneficio")]
[Index("Nome", Name = "UQ_Beneficio_Nome", IsUnique = true)]
public partial class Beneficio
{
    [Key]
    public int Id { get; set; }

    public int CategoriaId { get; set; }

    [StringLength(80)]
    public string Nome { get; set; } = null!;

    [StringLength(160)]
    public string? DescricaoCurta { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Icone { get; set; }

    public int Ordem { get; set; }

    public bool Ativo { get; set; }

    public bool Ficticio { get; set; }

    [ForeignKey("CategoriaId")]
    [InverseProperty("Beneficios")]
    public virtual BeneficioCategoria Categoria { get; set; } = null!;

    [ForeignKey("BeneficioId")]
    [InverseProperty("Beneficios")]
    public virtual ICollection<Vaga> Vagas { get; set; } = new List<Vaga>();
}
