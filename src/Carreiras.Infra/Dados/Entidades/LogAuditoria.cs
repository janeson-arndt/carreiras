using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Index("Tabela", "RegistroId", "DataHora", Name = "IX_LogAuditoria_Tabela_Registro")]
public partial class LogAuditoria
{
    [Key]
    public long Id { get; set; }

    public int? UsuarioRhId { get; set; }

    [StringLength(128)]
    public string Tabela { get; set; } = null!;

    public int RegistroId { get; set; }

    [StringLength(128)]
    public string Campo { get; set; } = null!;

    public string? ValorAntigo { get; set; }

    public string? ValorNovo { get; set; }

    [Precision(0)]
    public DateTime DataHora { get; set; }

    [ForeignKey("UsuarioRhId")]
    [InverseProperty("LogAuditoria")]
    public virtual UsuarioRh? UsuarioRh { get; set; }
}
