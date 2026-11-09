using System.Data;
using System.Net.Mail;
using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Email;
using Carreiras.Infra.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Usuarios;

public sealed record Resultado(bool Ok, string? Erro = null)
{
    public static Resultado Sucesso() => new(true);
    public static Resultado Falha(string erro) => new(false, erro);
}

public sealed record UsuarioItem(
    int Id, string Nome, string Email, string Perfil, bool Ativo, bool ConviteAceito, DateTime? UltimoAcessoEm)
{
    public SituacaoUsuario Situacao => RegrasUsuario.Situacao(Ativo, ConviteAceito);
    public bool ConsegueEntrar => Ativo && ConviteAceito;
}

/// <summary>Monta o link absoluto a partir do id do usuário no Identity e do token (a camada web codifica o token).</summary>
public delegate string MontarLink(string identityUserId, string token);

/// <summary>
/// Gestão de usuários do RH (RN16 a RN20). O administrador nunca define nem vê senha: a pessoa recebe um convite
/// e cria a própria. Usuário não é excluído, só desativado.
/// </summary>
public sealed class GestaoUsuarios
{
    private readonly AppDbContext _db;
    private readonly AuthDbContext _auth;
    private readonly UserManager<IdentityUser> _um;
    private readonly IEmailSender _email;

    public GestaoUsuarios(AppDbContext db, AuthDbContext auth, UserManager<IdentityUser> um, IEmailSender email)
    {
        _db = db; _auth = auth; _um = um; _email = email;
    }

    public async Task<List<UsuarioItem>> ListarAsync(CancellationToken ct = default)
    {
        var usuarios = await _db.UsuarioRhs.AsNoTracking().Include(u => u.Perfil)
            .OrderBy(u => u.Nome).ToListAsync(ct);
        var comSenha = await IdsComSenhaAsync(usuarios.Select(u => u.IdentityUserId), ct);
        return usuarios.Select(u => new UsuarioItem(u.Id, u.Nome, u.Email, u.Perfil.Nome, u.Ativo,
            u.IdentityUserId != null && comSenha.Contains(u.IdentityUserId), u.UltimoAcessoEm)).ToList();
    }

    /// <summary>Motivo pelo qual desativar o usuário está bloqueado agora (ou null). Só para a interface; o servidor revalida.</summary>
    public static string? MotivoBloqueioDesativar(UsuarioItem alvo, int atorId, IReadOnlyCollection<UsuarioItem> todos)
    {
        var proprio = RegrasUsuario.EhProprio(atorId, alvo.Id);
        var outros = todos.Count(u => u.Id != alvo.Id && u.Perfil == Perfis.Administrador && u.ConsegueEntrar);
        var ultimo = RegrasUsuario.PerderiaUltimoAdministrador(alvo.Perfil, alvo.ConsegueEntrar, outros);
        // Quem consulta a lista é sempre um administrador que entra; por isso o "último" só aparece na própria linha,
        // e aí os dois motivos valem juntos.
        if (proprio && ultimo) return RegrasUsuario.MotivoProprio + " " + RegrasUsuario.MotivoUltimoAdmin;
        if (proprio) return RegrasUsuario.MotivoProprio;
        return ultimo ? RegrasUsuario.MotivoUltimoAdmin : null;
    }

    public async Task<Resultado> CadastrarAsync(
        string nome, string email, string perfil, int atorId, MontarLink montarLink, CancellationToken ct = default)
    {
        nome = (nome ?? "").Trim();
        email = (email ?? "").Trim().ToLowerInvariant();
        if (nome.Length is 0 or > 120) return Resultado.Falha("Informe o nome, com até 120 caracteres.");
        if (!EmailValido(email)) return Resultado.Falha("Informe um e-mail válido, como nome@exemplo.com.");
        if (!Perfis.Todos.Contains(perfil)) return Resultado.Falha("Escolha um perfil.");

        if (await _db.UsuarioRhs.AnyAsync(u => u.Email == email, ct) || await _um.FindByEmailAsync(email) != null)
            return Resultado.Falha("Já existe um usuário com este e-mail.");

        var perfilId = await _db.PerfilRhs.Where(p => p.Nome == perfil).Select(p => p.Id).SingleAsync(ct);
        var identity = new IdentityUser { UserName = email, Email = email, EmailConfirmed = false, LockoutEnabled = true };
        var criado = await _um.CreateAsync(identity);   // sem senha: a pessoa cria pelo convite
        if (!criado.Succeeded) return Resultado.Falha("Não foi possível cadastrar este e-mail.");

        UsuarioRh novo;
        try
        {
            novo = new UsuarioRh
            {
                Nome = nome, Email = email, PerfilId = perfilId, Ativo = true,
                IdentityUserId = identity.Id, CriadoPorUsuarioRhId = atorId,
            };
            _db.UsuarioRhs.Add(novo);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            await _um.DeleteAsync(identity);
            throw;
        }

        await EnviarConviteAsync(novo, identity, atorId, montarLink, ct);
        return Resultado.Sucesso();
    }

    public async Task<Resultado> AlterarPerfilAsync(int id, string perfil, int atorId, CancellationToken ct = default)
    {
        if (!Perfis.Todos.Contains(perfil)) return Resultado.Falha("Escolha um perfil.");
        var r = await SobAdministradoresAsync(id, atorId, async c =>
        {
            var alvo = c.Alvo;
            if (c.Perfil == perfil) return Resultado.Sucesso();
            if (perfil != Perfis.Administrador && PerderiaUltimo(c))
                return Resultado.Falha(RegrasUsuario.MotivoUltimoAdmin);
            alvo.PerfilId = await _db.PerfilRhs.Where(p => p.Nome == perfil).Select(p => p.Id).SingleAsync(ct);
            return Resultado.Sucesso();
        }, ct);
        if (r.Ok) await RenovarSessoesAsync(id);   // os papéis da pessoa mudaram
        return r;
    }

    public async Task<Resultado> DesativarAsync(int id, int atorId, CancellationToken ct = default)
    {
        var r = await SobAdministradoresAsync(id, atorId, c =>
        {
            if (!c.Alvo.Ativo) return Task.FromResult(Resultado.Sucesso());
            if (PerderiaUltimo(c))
                return Task.FromResult(Resultado.Falha(RegrasUsuario.MotivoUltimoAdmin));
            c.Alvo.Ativo = false;
            return Task.FromResult(Resultado.Sucesso());
        }, ct);
        if (r.Ok) await RenovarSessoesAsync(id);   // encerra as sessões abertas (carimbo de segurança)
        return r;
    }

    public async Task<Resultado> ReativarAsync(int id, int atorId, CancellationToken ct = default)
    {
        var r = await SobAdministradoresAsync(id, atorId, c =>
        {
            c.Alvo.Ativo = true;
            return Task.FromResult(Resultado.Sucesso());
        }, ct);
        if (r.Ok) await RenovarSessoesAsync(id);
        return r;
    }

    public async Task<Resultado> ReenviarConviteAsync(int id, int atorId, MontarLink montarLink, CancellationToken ct = default)
    {
        var (u, identity) = await CarregarAsync(id, ct);
        if (u == null || identity == null) return Resultado.Falha("Usuário não encontrado.");
        if (!u.Ativo) return Resultado.Falha("Reative o usuário antes de reenviar o convite.");
        if (identity.PasswordHash != null) return Resultado.Falha("Este usuário já aceitou o convite.");
        await EnviarConviteAsync(u, identity, atorId, montarLink, ct);
        return Resultado.Sucesso();
    }

    public async Task<Resultado> EnviarNovaSenhaAsync(int id, int atorId, MontarLink montarLink, CancellationToken ct = default)
    {
        var (u, identity) = await CarregarAsync(id, ct);
        if (u == null || identity == null) return Resultado.Falha("Usuário não encontrado.");
        if (!u.Ativo) return Resultado.Falha("Reative o usuário antes de enviar o link.");
        if (identity.PasswordHash == null) return Resultado.Falha("Este usuário ainda não aceitou o convite. Reenvie o convite.");

        var token = await _um.GeneratePasswordResetTokenAsync(identity);
        await _email.EnviarAsync(u.Email, "Crie uma nova senha no Portal de Carreiras",
            $"Olá, {u.Nome}.\n\nUse o link abaixo para criar uma nova senha. Ele expira e só pode ser usado uma vez.\n\n{montarLink(identity.Id, token)}\n\nSe você não pediu, ignore esta mensagem.", ct);
        await RegistrarFatoAsync(atorId, u.Id, "Link de nova senha enviado", ct);
        return Resultado.Sucesso();
    }

    // ---------------------------------------------------------------- internos

    private sealed record ContextoAlvo(UsuarioRh Alvo, string Perfil, bool AlvoEntra, int OutrosAdminsQueEntram);

    private static bool PerderiaUltimo(ContextoAlvo c) =>
        RegrasUsuario.PerderiaUltimoAdministrador(c.Perfil, c.AlvoEntra, c.OutrosAdminsQueEntram);

    /// <summary>
    /// RN16/RN17: lê e trava as linhas dos administradores, confere as regras e altera, tudo na MESMA transação.
    /// Sem o UPDLOCK/HOLDLOCK, duas desativações simultâneas poderiam deixar o portal sem administrador.
    /// </summary>
    private async Task<Resultado> SobAdministradoresAsync(
        int id, int atorId, Func<ContextoAlvo, Task<Resultado>> aplicar, CancellationToken ct)
    {
        if (RegrasUsuario.EhProprio(atorId, id)) return Resultado.Falha(RegrasUsuario.MotivoProprio);

        await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var admins = await _db.UsuarioRhs
            .FromSqlRaw("SELECT * FROM dbo.UsuarioRh WITH (UPDLOCK, HOLDLOCK) WHERE PerfilId = (SELECT Id FROM dbo.PerfilRh WHERE Nome = N'ADMINISTRADOR')")
            .ToListAsync(ct);
        var alvo = await _db.UsuarioRhs.Include(u => u.Perfil).SingleOrDefaultAsync(u => u.Id == id, ct);
        if (alvo == null) return Resultado.Falha("Usuário não encontrado.");

        var comSenha = await IdsComSenhaAsync(admins.Select(a => a.IdentityUserId).Append(alvo.IdentityUserId), ct);
        bool Entra(UsuarioRh u) => u.Ativo && u.IdentityUserId != null && comSenha.Contains(u.IdentityUserId);
        var outros = admins.Count(a => a.Id != alvo.Id && Entra(a));

        var resultado = await aplicar(new ContextoAlvo(alvo, alvo.Perfil.Nome, Entra(alvo), outros));
        if (!resultado.Ok) return resultado;

        await RegistrarSeMudouAsync(alvo, ct);
        await tx.CommitAsync(ct);
        return Resultado.Sucesso();
    }

    private async Task RegistrarSeMudouAsync(UsuarioRh alvo, CancellationToken ct)
    {
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);   // o interceptador grava a auditoria
    }

    private async Task EnviarConviteAsync(UsuarioRh u, IdentityUser identity, int atorId, MontarLink montarLink, CancellationToken ct)
    {
        var token = await _um.GeneratePasswordResetTokenAsync(identity);
        await _email.EnviarAsync(u.Email, "Convite para o Portal de Carreiras",
            $"Olá, {u.Nome}.\n\nVocê foi convidado(a) para o portal administrativo de carreiras. Use o link abaixo para criar a sua senha. Ele expira e só pode ser usado uma vez.\n\n{montarLink(identity.Id, token)}\n", ct);
        await RegistrarFatoAsync(atorId, u.Id, "Convite enviado", ct);   // só o fato: nunca o link
    }

    private async Task RegistrarFatoAsync(int atorId, int registroId, string campo, CancellationToken ct)
    {
        _db.LogAuditoria.Add(new LogAuditoria { UsuarioRhId = atorId, Tabela = "UsuarioRh", RegistroId = registroId, Campo = campo });
        await _db.SaveChangesAsync(ct);
    }

    private async Task<(UsuarioRh? U, IdentityUser? Identity)> CarregarAsync(int id, CancellationToken ct)
    {
        var u = await _db.UsuarioRhs.SingleOrDefaultAsync(x => x.Id == id, ct);
        var identity = u?.IdentityUserId == null ? null : await _um.FindByIdAsync(u.IdentityUserId);
        return (u, identity);
    }

    private async Task RenovarSessoesAsync(int id)
    {
        var ident = await _db.UsuarioRhs.AsNoTracking().Where(u => u.Id == id).Select(u => u.IdentityUserId).SingleOrDefaultAsync();
        var identity = ident == null ? null : await _um.FindByIdAsync(ident);
        if (identity != null) await _um.UpdateSecurityStampAsync(identity);
    }

    private async Task<HashSet<string>> IdsComSenhaAsync(IEnumerable<string?> ids, CancellationToken ct)
    {
        var lista = ids.Where(i => i != null).Select(i => i!).Distinct().ToList();
        var comSenha = await _auth.Users.AsNoTracking()
            .Where(x => lista.Contains(x.Id) && x.PasswordHash != null).Select(x => x.Id).ToListAsync(ct);
        return comSenha.ToHashSet();
    }

    private static bool EmailValido(string email)
    {
        if (email.Length is 0 or > 256) return false;
        try { var a = new MailAddress(email); return a.Address == email && email.Contains('.') ; }
        catch (FormatException) { return false; }
    }
}
