using System.Security.Claims;
using Carreiras.Infra.Auditoria;
using Carreiras.Infra.Dados;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Carreiras.Web.Seguranca;

public static class Claims
{
    public const string UsuarioRhId = "uid";
    public const string Nome = "nome";
}

/// <summary>Papel (perfil do RH) e id do UsuarioRh entram como claims. Usuário desativado não recebe papel algum.</summary>
public sealed class PerfilClaimsFactory : UserClaimsPrincipalFactory<IdentityUser, IdentityRole>
{
    private readonly AppDbContext _db;

    public PerfilClaimsFactory(
        UserManager<IdentityUser> um, RoleManager<IdentityRole> rm,
        IOptions<IdentityOptions> opt, AppDbContext db) : base(um, rm, opt) => _db = db;

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(IdentityUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        var u = await _db.UsuarioRhs.AsNoTracking().Include(x => x.Perfil)
            .SingleOrDefaultAsync(x => x.IdentityUserId == user.Id);
        if (u is { Ativo: true })
        {
            id.AddClaim(new Claim(ClaimTypes.Role, u.Perfil.Nome));
            id.AddClaim(new Claim(Claims.UsuarioRhId, u.Id.ToString()));
            id.AddClaim(new Claim(Claims.Nome, u.Nome));
        }
        return id;
    }
}

public sealed class UsuarioAuditoriaHttp : IUsuarioAuditoria
{
    private readonly IHttpContextAccessor _http;
    public UsuarioAuditoriaHttp(IHttpContextAccessor http) => _http = http;

    public int? UsuarioRhId =>
        int.TryParse(_http.HttpContext?.User.FindFirstValue(Claims.UsuarioRhId), out var id) ? id : null;
}

public static class UsuarioExtensoes
{
    public static int UsuarioRhId(this ClaimsPrincipal u) =>
        int.Parse(u.FindFirstValue(Claims.UsuarioRhId) ?? throw new InvalidOperationException("Sem usuário do RH."));
}
