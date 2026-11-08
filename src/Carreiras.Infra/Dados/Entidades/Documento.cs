using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Documento")]
public partial class Documento
{
    [Key]
    public int Id { get; set; }

    public int ConteudoId { get; set; }

    [StringLength(150)]
    public string Titulo { get; set; } = null!;

    [StringLength(400)]
    public string Caminho { get; set; } = null!;

    [ForeignKey("ConteudoId")]
    [InverseProperty("Documentos")]
    public virtual ConteudoInstitucional Conteudo { get; set; } = null!;
}
