using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Usuarios;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

[Collection("banco")]
public class UsuarioTestes
{
    private readonly BancoTeste _b;
    public UsuarioTestes(BancoTeste b) => _b = b;

    private static string LinkFalso(string uid, string token) => $"https://teste.local/definir-senha?uid={uid}&token=SEGREDO-{token}";

    private async Task<Resultado> Desativar(int id, int ator)
    {
        using var s = _b.Escopo();
        return await s.ServiceProvider.GetRequiredService<GestaoUsuarios>().DesativarAsync(id, ator);
    }

    private Task<int> AdminsQueEntram() => _b.Escalar<int>(@"
SELECT COUNT(*) FROM dbo.UsuarioRh u JOIN dbo.PerfilRh p ON p.Id = u.PerfilId JOIN dbo.AspNetUsers a ON a.Id = u.IdentityUserId
WHERE p.Nome = 'ADMINISTRADOR' AND u.Ativo = 1 AND a.PasswordHash IS NOT NULL");

    [Fact]
    public async Task Ninguem_desativa_a_si_mesmo_nem_muda_o_proprio_perfil()
    {
        await _b.DesativarTodosAsync();
        var a = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var b = await _b.CriarUsuarioAsync(Perfis.Administrador);
        Assert.False((await Desativar(a, a)).Ok);
        using var s = _b.Escopo();
        var r = await s.ServiceProvider.GetRequiredService<GestaoUsuarios>().AlterarPerfilAsync(a, Perfis.Leitura, a);
        Assert.False(r.Ok);
        Assert.Equal(RegrasUsuario.MotivoProprio, r.Erro);
    }

    [Fact]
    public async Task Nao_desativa_nem_rebaixa_o_ultimo_administrador()
    {
        await _b.DesativarTodosAsync();
        var unico = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var analista = await _b.CriarUsuarioAsync(Perfis.Analista);

        var r = await Desativar(unico, analista);   // outra sessão (outro usuário) tenta
        Assert.False(r.Ok);
        Assert.Equal(RegrasUsuario.MotivoUltimoAdmin, r.Erro);

        using var s = _b.Escopo();
        var rebaixa = await s.ServiceProvider.GetRequiredService<GestaoUsuarios>().AlterarPerfilAsync(unico, Perfis.Leitura, analista);
        Assert.False(rebaixa.Ok);
        Assert.Equal(1, await AdminsQueEntram());
    }

    [Fact]
    public async Task Convite_pendente_nao_conta_como_administrador_que_consegue_entrar()
    {
        await _b.DesativarTodosAsync();
        var ativo = await _b.CriarUsuarioAsync(Perfis.Administrador);
        await _b.CriarUsuarioAsync(Perfis.Administrador, conviteAceito: false);   // convite pendente
        var outro = await _b.CriarUsuarioAsync(Perfis.Analista);

        var r = await Desativar(ativo, outro);
        Assert.False(r.Ok);   // o pendente não pode assumir
        Assert.Equal(1, await AdminsQueEntram());
    }

    [Fact]
    public async Task Com_dois_administradores_um_pode_desativar_o_outro_e_depois_reativar()
    {
        await _b.DesativarTodosAsync();
        var a = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var b = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var carimboAntes = await Carimbo(b);

        Assert.True((await Desativar(b, a)).Ok);
        Assert.NotEqual(carimboAntes, await Carimbo(b));   // sessões abertas de b perdem a validade
        Assert.Equal(1, await AdminsQueEntram());

        using var s = _b.Escopo();
        Assert.True((await s.ServiceProvider.GetRequiredService<GestaoUsuarios>().ReativarAsync(b, a)).Ok);
        Assert.Equal(2, await AdminsQueEntram());
    }

    [Fact]
    public async Task Duas_desativacoes_simultaneas_de_administradores_nao_deixam_o_portal_sem_administrador()
    {
        for (var rodada = 0; rodada < 8; rodada++)
        {
            await _b.DesativarTodosAsync();
            var a = await _b.CriarUsuarioAsync(Perfis.Administrador);
            var b = await _b.CriarUsuarioAsync(Perfis.Administrador);

            // um terceiro usuário (analista) não conta; a e b tentam desativar um ao outro ao mesmo tempo
            var porta = new TaskCompletionSource();
            async Task<Resultado> Tenta(int alvo, int ator) { await porta.Task; return await Desativar(alvo, ator); }
            var t1 = Task.Run(() => Tenta(b, a));
            var t2 = Task.Run(() => Tenta(a, b));
            porta.SetResult();
            var rs = await Task.WhenAll(t1, t2);

            Assert.Equal(1, rs.Count(r => r.Ok));
            Assert.True(await AdminsQueEntram() >= 1, $"rodada {rodada}: ficou sem administrador");
        }
    }

    [Fact]
    public async Task Cadastro_envia_convite_sem_senha_e_a_auditoria_nao_guarda_link_nem_senha()
    {
        await _b.DesativarTodosAsync();
        var admin = await _b.CriarUsuarioAsync(Perfis.Administrador);
        _b.Ator.UsuarioRhId = admin;
        var email = $"novo-{Guid.NewGuid():N}@teste.local";
        using (var s = _b.Escopo())
        {
            var r = await s.ServiceProvider.GetRequiredService<GestaoUsuarios>().CadastrarAsync("Pessoa Nova", email, Perfis.Leitura, admin, LinkFalso);
            Assert.True(r.Ok, r.Erro);
        }
        Assert.Equal(0, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.AspNetUsers WHERE Email='{email}' AND PasswordHash IS NOT NULL"));
        var envio = Assert.Single(_b.Emails.Enviados, e => e.Para == email);
        Assert.Contains("SEGREDO-", envio.Corpo);   // o link vai por e-mail...

        // ...e nunca para a auditoria
        var tudo = await _b.Escalar<string>("SELECT COALESCE(STRING_AGG(CONCAT(Campo,'|',ValorAntigo,'|',ValorNovo), ';'),'') FROM dbo.LogAuditoria WHERE Tabela='UsuarioRh'");
        Assert.DoesNotContain("SEGREDO", tudo);
        Assert.DoesNotContain("token", tudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SenhaHash", tudo);
        Assert.DoesNotContain("PasswordHash", tudo);
        Assert.Contains("Convite enviado", tudo);
    }

    [Fact]
    public async Task Email_duplicado_e_recusado_e_perfil_invalido_tambem()
    {
        var admin = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var existente = $"dup-{Guid.NewGuid():N}@teste.local";
        await _b.CriarUsuarioAsync(Perfis.Leitura, email: existente);
        using var s = _b.Escopo();
        var g = s.ServiceProvider.GetRequiredService<GestaoUsuarios>();
        Assert.False((await g.CadastrarAsync("X", existente, Perfis.Leitura, admin, LinkFalso)).Ok);
        Assert.False((await g.CadastrarAsync("X", "invalido", Perfis.Leitura, admin, LinkFalso)).Ok);
        Assert.False((await g.CadastrarAsync("X", "ok@teste.local", "SUPER", admin, LinkFalso)).Ok);
    }

    [Fact]
    public async Task Mudanca_de_perfil_e_ativacao_vao_para_a_auditoria()
    {
        await _b.DesativarTodosAsync();
        var admin = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var alvo = await _b.CriarUsuarioAsync(Perfis.Leitura);
        _b.Ator.UsuarioRhId = admin;
        using (var s = _b.Escopo())
        {
            var g = s.ServiceProvider.GetRequiredService<GestaoUsuarios>();
            Assert.True((await g.AlterarPerfilAsync(alvo, Perfis.Analista, admin)).Ok);
            Assert.True((await g.DesativarAsync(alvo, admin)).Ok);
        }
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='UsuarioRh' AND RegistroId={alvo} AND Campo='PerfilId' AND UsuarioRhId={admin}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='UsuarioRh' AND RegistroId={alvo} AND Campo='Ativo' AND ValorAntigo=N'Sim' AND ValorNovo=N'Não'"));
    }

    [Fact]
    public async Task Nao_existe_acao_de_excluir_nem_campo_de_senha_no_cadastro()
    {
        var metodos = typeof(GestaoUsuarios).GetMethods().Select(m => m.Name);
        Assert.DoesNotContain(metodos, n => n.Contains("Excluir", StringComparison.OrdinalIgnoreCase) || n.Contains("Remover", StringComparison.OrdinalIgnoreCase));
        var cadastrar = typeof(GestaoUsuarios).GetMethod(nameof(GestaoUsuarios.CadastrarAsync))!;
        Assert.DoesNotContain(cadastrar.GetParameters(), p => p.Name!.Contains("senha", StringComparison.OrdinalIgnoreCase));
        var pagina = typeof(Carreiras.Web.Areas.Admin.Pages.Usuarios.NovoModel);
        Assert.DoesNotContain(pagina.GetProperties(), p => p.Name.Contains("Senha", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> Carimbo(int usuarioId) =>
        await _b.Escalar<string>($"SELECT a.SecurityStamp FROM dbo.AspNetUsers a JOIN dbo.UsuarioRh u ON u.IdentityUserId=a.Id WHERE u.Id={usuarioId}");
}
