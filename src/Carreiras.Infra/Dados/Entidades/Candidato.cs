using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Candidato")]
[Index("Email", Name = "UQ_Candidato_Email", IsUnique = true)]
public partial class Candidato
{
    [Key]
    public int Id { get; set; }

    [StringLength(256)]
    public string Email { get; set; } = null!;

    [StringLength(120)]
    public string Nome { get; set; } = null!;

    [StringLength(20)]
    public string? Telefone { get; set; }

    [StringLength(80)]
    public string? Cidade { get; set; }

    [StringLength(2)]
    [Unicode(false)]
    public string? Uf { get; set; }

    public bool EmailConfirmado { get; set; }

    [Precision(0)]
    public DateTime CriadoEm { get; set; }

    [Precision(0)]
    public DateTime RetencaoAte { get; set; }

    [Precision(0)]
    public DateTime? AnonimizadoEm { get; set; }

    [InverseProperty("Candidato")]
    public virtual ICollection<Candidatura> Candidaturas { get; set; } = new List<Candidatura>();

    [InverseProperty("Candidato")]
    public virtual ICollection<ConfirmacaoEmail> ConfirmacaoEmails { get; set; } = new List<ConfirmacaoEmail>();

    [InverseProperty("Candidato")]
    public virtual ICollection<ConsentimentoLgpd> ConsentimentoLgpds { get; set; } = new List<ConsentimentoLgpd>();

    [InverseProperty("Candidato")]
    public virtual ICollection<Curriculo> Curriculos { get; set; } = new List<Curriculo>();

    [InverseProperty("Candidato")]
    public virtual ICollection<SolicitacaoTitular> SolicitacaoTitulars { get; set; } = new List<SolicitacaoTitular>();
}
