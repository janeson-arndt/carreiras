using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

/// <summary>
/// Colunas acrescentadas pelo db/003: a candidatura que aguarda o clique no link. Fica em arquivo à parte para o
/// arquivo gerado a partir do banco poder ser refeito sem perder estas propriedades.
/// </summary>
public partial class ConfirmacaoEmail
{
    [Precision(0)]
    public DateTime CriadaEm { get; set; }

    public int? VagaId { get; set; }

    public int? CurriculoId { get; set; }

    [Column(TypeName = "decimal(10, 2)")]
    public decimal? PretensaoSalarial { get; set; }

    [StringLength(120)]
    public string? Nome { get; set; }

    [StringLength(20)]
    public string? Telefone { get; set; }

    [StringLength(80)]
    public string? Cidade { get; set; }

    [StringLength(2)]
    [Unicode(false)]
    public string? Uf { get; set; }
}
