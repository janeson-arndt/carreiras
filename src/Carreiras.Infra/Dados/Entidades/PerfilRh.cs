using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados.Entidades;

[Table("PerfilRh")]
[Index("Nome", Name = "UQ_PerfilRh_Nome", IsUnique = true)]
public partial class PerfilRh
{
    [Key]
    public int Id { get; set; }

    [StringLength(40)]
    public string Nome { get; set; } = null!;

    [InverseProperty("Perfil")]
    public virtual ICollection<UsuarioRh> UsuarioRhs { get; set; } = new List<UsuarioRh>();
}
