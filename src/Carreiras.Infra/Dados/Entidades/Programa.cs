using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Programa")]
[Index("Nome", Name = "UQ_Programa_Nome", IsUnique = true)]
public partial class Programa
{
    [Key]
    public int Id { get; set; }

    [StringLength(100)]
    public string Nome { get; set; } = null!;

    [StringLength(600)]
    public string? Descricao { get; set; }

    [StringLength(200)]
    public string? PublicoAlvo { get; set; }

    public bool Ativo { get; set; }

    public bool Ficticio { get; set; }

    [InverseProperty("Programa")]
    public virtual ICollection<Vaga> Vagas { get; set; } = new List<Vaga>();
}
