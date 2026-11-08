using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Cadastros;
using Carreiras.Infra.Usuarios;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

[Collection("banco")]
public class ConteudoTestes
{
    private readonly BancoTeste _b;
    public ConteudoTestes(BancoTeste b) => _b = b;

    private async Task<(Resultado R, int? Id, Dictionary<string, string> E)> Salvar(ConteudoForm f, int ator)
    {
        _b.Ator.UsuarioRhId = ator;
        using var s = _b.Escopo();
        return await s.ServiceProvider.GetRequiredService<ConteudoServico>().SalvarAsync(f);
    }

    private static string NovaSecao() => "teste-" + Guid.NewGuid().ToString("N")[..10];

    private Task<int> Logs(int id, string? campo = null) =>
        _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='ConteudoInstitucional' AND RegistroId={id}" + (campo == null ? "" : $" AND Campo=N'{campo}'"));

    [Fact]
    public async Task Cria_conteudo_publicado_e_audita_a_criacao()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var (r, id, _) = await Salvar(new ConteudoForm { Secao = NovaSecao(), Titulo = "Novo", Texto = "Texto", Ordem = 9 }, ator);
        Assert.True(r.Ok, r.Erro);
        Assert.Equal(1, await _b.Escalar<int>($"SELECT CAST(Publicado AS int) FROM dbo.ConteudoInstitucional WHERE Id={id}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='ConteudoInstitucional' AND RegistroId={id} AND Campo=N'(criado)' AND UsuarioRhId={ator}"));
    }

    [Fact]
    public async Task Valida_secao_titulo_texto_ordem_e_secao_duplicada()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var (r, _, e) = await Salvar(new ConteudoForm(), ator);
        Assert.False(r.Ok);
        Assert.True(e.ContainsKey("Secao") && e.ContainsKey("Titulo") && e.ContainsKey("Texto"));

        foreach (var ruim in new[] { "Quem Somos", "com_underscore", "acentuação", "-inicio", "fim-", "dois--hifens", new string('a', 41) })
        {
            (_, _, e) = await Salvar(new ConteudoForm { Secao = ruim, Titulo = "t", Texto = "x" }, ator);
            Assert.True(e.ContainsKey("Secao"), ruim);
        }
        (_, _, e) = await Salvar(new ConteudoForm { Secao = NovaSecao(), Titulo = new string('t', 121), Texto = "x" }, ator);
        Assert.True(e.ContainsKey("Titulo"));
        (_, _, e) = await Salvar(new ConteudoForm { Secao = NovaSecao(), Titulo = "t", Texto = "x", Ordem = 10000 }, ator);
        Assert.True(e.ContainsKey("Ordem"));
        (_, _, e) = await Salvar(new ConteudoForm { Secao = "sobre", Titulo = "t", Texto = "x" }, ator);   // já existe no seed
        Assert.True(e.ContainsKey("Secao"));
    }

    [Fact]
    public async Task Secao_de_conteudo_existente_nao_muda()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var secao = NovaSecao();
        var (_, id, _) = await Salvar(new ConteudoForm { Secao = secao, Titulo = "t", Texto = "x" }, ator);
        var (r, _, _) = await Salvar(new ConteudoForm { Id = id, Secao = "outra-secao", Titulo = "t2", Texto = "x" }, ator);
        Assert.True(r.Ok);
        Assert.Equal(secao, await _b.Escalar<string>($"SELECT Secao FROM dbo.ConteudoInstitucional WHERE Id={id}"));
        Assert.Equal(0, await Logs(id!.Value, "Secao"));
    }

    [Fact]
    public async Task Editar_grava_antigo_e_novo_e_salvar_sem_mudar_nao_grava_inclusive_com_CRLF()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var (_, id, _) = await Salvar(new ConteudoForm { Secao = NovaSecao(), Titulo = "Título", Texto = "linha 1\nlinha 2", Ordem = 3 }, ator);
        var antes = await Logs(id!.Value);

        // o navegador envia quebra de linha CRLF: o mesmo texto não pode gerar log
        var (r, _, _) = await Salvar(new ConteudoForm { Id = id, Titulo = "Título", Texto = "linha 1\r\nlinha 2\r\n", Ordem = 3, Publicado = true }, ator);
        Assert.True(r.Ok);
        Assert.Equal(antes, await Logs(id.Value));

        await Salvar(new ConteudoForm { Id = id, Titulo = "Título novo", Texto = "linha 1\nlinha 2", Ordem = 3, Publicado = true }, ator);
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='ConteudoInstitucional' AND RegistroId={id} AND Campo=N'Titulo' AND ValorAntigo=N'Título' AND ValorNovo=N'Título novo' AND UsuarioRhId={ator}"));
    }

    [Fact]
    public async Task Despublica_e_republica_sem_excluir_e_audita()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        _b.Ator.UsuarioRhId = ator;
        var (_, id, _) = await Salvar(new ConteudoForm { Secao = NovaSecao(), Titulo = "t", Texto = "x" }, ator);
        using (var s = _b.Escopo())
            Assert.True((await s.ServiceProvider.GetRequiredService<ConteudoServico>().AlternarPublicadoAsync(id!.Value, false)).Ok);
        Assert.Equal(0, await _b.Escalar<int>($"SELECT CAST(Publicado AS int) FROM dbo.ConteudoInstitucional WHERE Id={id}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='ConteudoInstitucional' AND RegistroId={id} AND Campo='Publicado' AND ValorAntigo=N'Sim' AND ValorNovo=N'Não'"));
        using (var s = _b.Escopo())
            Assert.True((await s.ServiceProvider.GetRequiredService<ConteudoServico>().AlternarPublicadoAsync(id!.Value, true)).Ok);

        var metodos = typeof(ConteudoServico).GetMethods().Select(m => m.Name);
        Assert.DoesNotContain(metodos, n => n.Contains("Excluir", StringComparison.OrdinalIgnoreCase) || n.Contains("Remover", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Texto_de_exemplo_pode_virar_definitivo()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var id = await _b.Escalar<int>("SELECT Id FROM dbo.ConteudoInstitucional WHERE Secao='privacidade'");
        using var s = _b.Escopo();
        var svc = s.ServiceProvider.GetRequiredService<ConteudoServico>();
        var f = (await svc.ObterAsync(id))!;
        Assert.True(f.Ficticio);
        f.Ficticio = false;
        _b.Ator.UsuarioRhId = ator;
        Assert.True((await svc.SalvarAsync(f)).Item1.Ok);
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='ConteudoInstitucional' AND RegistroId={id} AND Campo='Ficticio' AND ValorAntigo=N'Sim'"));
        // devolve ao estado do seed para não afetar outros testes
        f.Ficticio = true;
        await svc.SalvarAsync(f);
    }
}
