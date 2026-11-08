using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Identidade;

/// <summary>Credenciais do RH (ASP.NET Identity). O negócio (perfil, situação) fica em UsuarioRh.</summary>
public class AuthDbContext : IdentityDbContext<IdentityUser>
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }
}
