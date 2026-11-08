using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Unidade")]
[Index("Nome", Name = "UQ_Unidade_Nome", IsUnique = true)]
public partial class Unidade
{
    [Key]
    public int Id { get; set; }

    [StringLength(100)]
    public string Nome { get; set; } = null!;

    [StringLength(25)]
    [Unicode(false)]
    public string Tipo { get; set; } = null!;

    [StringLength(150)]
    public string Endereco { get; set; } = null!;

    [StringLength(80)]
    public string? Bairro { get; set; }

    [StringLength(80)]
    public string Cidade { get; set; } = null!;

    [StringLength(2)]
    [Unicode(false)]
    public string Uf { get; set; } = null!;

    [StringLength(9)]
    [Unicode(false)]
    public string? Cep { get; set; }

    [StringLength(60)]
    public string? Telefone { get; set; }

    public bool Ativo { get; set; }

    [InverseProperty("Unidade")]
    public virtual ICollection<Vaga> Vagas { get; set; } = new List<Vaga>();
}
