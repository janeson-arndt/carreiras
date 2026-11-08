using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("ConteudoInstitucional")]
[Index("Secao", Name = "UQ_ConteudoInstitucional_Secao", IsUnique = true)]
public partial class ConteudoInstitucional
{
    [Key]
    public int Id { get; set; }

    [StringLength(40)]
    [Unicode(false)]
    public string Secao { get; set; } = null!;

    [StringLength(120)]
    public string Titulo { get; set; } = null!;

    public string Texto { get; set; } = null!;

    public int Ordem { get; set; }

    public bool Publicado { get; set; }

    public bool Ficticio { get; set; }

    [InverseProperty("Conteudo")]
    public virtual ICollection<Documento> Documentos { get; set; } = new List<Documento>();
}
