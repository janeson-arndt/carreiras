using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Web.Seguranca;

/// <summary>
/// Cria o primeiro administrador, ou recupera o acesso quando o portal ficou sem administrador.
/// A senha vem de Admin:Senha (user-secrets) e nunca é exibida nem gravada em log.
/// </summary>
public static class CriarAdministrador
{
    public static async Task<int> ExecutarAsync(IServiceProvider raiz, IConfiguration config, string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Uso: dotnet run --project src/Carreiras.Web -- criar-admin \"Nome\" email@exemplo.com");
            return 2;
        }
        var nome = args[1].Trim();
        var email = args[2].Trim().ToLowerInvariant();
        var senha = config["Admin:Senha"];
        if (string.IsNullOrWhiteSpace(senha))
        {
            Console.Error.WriteLine("Defina a senha: dotnet user-secrets set \"Admin:Senha\" \"<senha>\" --project src/Carreiras.Web");
            return 2;
        }

        using var escopo = raiz.CreateScope();
        var sp = escopo.ServiceProvider;
        var um = sp.GetRequiredService<UserManager<IdentityUser>>();
        var db = sp.GetRequiredService<AppDbContext>();

        var perfilId = await db.PerfilRhs.Where(p => p.Nome == Perfis.Administrador).Select(p => p.Id).SingleAsync();
        var identity = await um.FindByEmailAsync(email);
        if (identity == null)
        {
            identity = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true, LockoutEnabled = true };
            var r = await um.CreateAsync(identity, senha);
            if (!r.Succeeded) return Falhar(r);
        }
        else
        {
            // Recuperação: define nova senha para a conta existente.
            var token = await um.GeneratePasswordResetTokenAsync(identity);
            var r = await um.ResetPasswordAsync(identity, token, senha);
            if (!r.Succeeded) return Falhar(r);
            await um.SetLockoutEndDateAsync(identity, null);
        }

        var u = await db.UsuarioRhs.SingleOrDefaultAsync(x => x.Email == email);
        if (u == null)
            db.UsuarioRhs.Add(new UsuarioRh { Nome = nome, Email = email, PerfilId = perfilId, Ativo = true, IdentityUserId = identity.Id });
        else
        {
            u.Nome = nome; u.PerfilId = perfilId; u.Ativo = true; u.IdentityUserId = identity.Id;
        }
        await db.SaveChangesAsync();
        await um.UpdateSecurityStampAsync(identity);
        Console.WriteLine($"Administrador pronto: {email}");
        return 0;
    }

    private static int Falhar(IdentityResult r)
    {
        Console.Error.WriteLine("Não foi possível: " + string.Join(" ", r.Errors.Select(e => e.Description)));
        return 1;
    }
}
