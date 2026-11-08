using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("Vaga")]
[Index("Status", "UnidadeId", "AreaId", "TurnoId", Name = "IX_Vaga_Listagem")]
[Index("Slug", Name = "UQ_Vaga_Slug", IsUnique = true)]
public partial class Vaga
{
    [Key]
    public int Id { get; set; }

    [StringLength(150)]
    public string Slug { get; set; } = null!;

    [StringLength(150)]
    public string Titulo { get; set; } = null!;

    public string Descricao { get; set; } = null!;

    public int UnidadeId { get; set; }

    public int AreaId { get; set; }

    public int TurnoId { get; set; }

    public int TipoContratoId { get; set; }

    public int ModalidadeId { get; set; }

    public int? ProgramaId { get; set; }

    [StringLength(15)]
    [Unicode(false)]
    public string Status { get; set; } = null!;

    [Column(TypeName = "decimal(10, 2)")]
    public decimal? SalarioMin { get; set; }

    [Column(TypeName = "decimal(10, 2)")]
    public decimal? SalarioMax { get; set; }

    public bool ExibirSalario { get; set; }

    [Precision(0)]
    public DateTime? PublicadaEm { get; set; }

    [Precision(0)]
    public DateTime CriadaEm { get; set; }

    [ForeignKey("AreaId")]
    [InverseProperty("Vagas")]
    public virtual Area Area { get; set; } = null!;

    [InverseProperty("Vaga")]
    public virtual ICollection<Candidatura> Candidaturas { get; set; } = new List<Candidatura>();

    [ForeignKey("ModalidadeId")]
    [InverseProperty("Vagas")]
    public virtual Modalidade Modalidade { get; set; } = null!;

    [ForeignKey("ProgramaId")]
    [InverseProperty("Vagas")]
    public virtual Programa? Programa { get; set; }

    [ForeignKey("TipoContratoId")]
    [InverseProperty("Vagas")]
    public virtual TipoContrato TipoContrato { get; set; } = null!;

    [ForeignKey("TurnoId")]
    [InverseProperty("Vagas")]
    public virtual Turno Turno { get; set; } = null!;

    [ForeignKey("UnidadeId")]
    [InverseProperty("Vagas")]
    public virtual Unidade Unidade { get; set; } = null!;

    [InverseProperty("Vaga")]
    public virtual ICollection<VagaAtividade> VagaAtividades { get; set; } = new List<VagaAtividade>();

    [InverseProperty("Vaga")]
    public virtual ICollection<VagaDiferencial> VagaDiferencials { get; set; } = new List<VagaDiferencial>();

    [InverseProperty("Vaga")]
    public virtual ICollection<VagaRequisito> VagaRequisitos { get; set; } = new List<VagaRequisito>();

    [InverseProperty("Vaga")]
    public virtual ICollection<VagaStatusHist> VagaStatusHists { get; set; } = new List<VagaStatusHist>();

    [ForeignKey("VagaId")]
    [InverseProperty("Vagas")]
    public virtual ICollection<Beneficio> Beneficios { get; set; } = new List<Beneficio>();
}
