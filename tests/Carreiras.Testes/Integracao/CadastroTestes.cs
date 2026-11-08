using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Cadastros;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

[Collection("banco")]
public class CadastroTestes
{
    private readonly BancoTeste _b;
    public CadastroTestes(BancoTeste b) => _b = b;

    private async Task<int> CategoriaAsync() => await _b.Escalar<int>("SELECT MIN(Id) FROM dbo.BeneficioCategoria");

    private async Task<(Carreiras.Infra.Usuarios.Resultado R, int? Id, Dictionary<string, string> E)> SalvarBen(BeneficioForm f, int ator)
    {
        _b.Ator.UsuarioRhId = ator;
        using var s = _b.Escopo();
        return await s.ServiceProvider.GetRequiredService<CadastroServico>().SalvarBeneficioAsync(f);
    }

    private async Task<(Carreiras.Infra.Usuarios.Resultado R, int? Id, Dictionary<string, string> E)> SalvarProg(ProgramaForm f, int ator)
    {
        _b.Ator.UsuarioRhId = ator;
        using var s = _b.Escopo();
        return await s.ServiceProvider.GetRequiredService<CadastroServico>().SalvarProgramaAsync(f);
    }

    private Task<int> Logs(string tabela, int id, string? campo = null) =>
        _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='{tabela}' AND RegistroId={id}" + (campo == null ? "" : $" AND Campo=N'{campo}'"));

    [Fact]
    public async Task Beneficio_e_criado_ativo_e_a_criacao_vai_para_a_auditoria()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var nome = "Benefício " + Guid.NewGuid().ToString("N")[..8];
        var (r, id, _) = await SalvarBen(new BeneficioForm { Nome = nome, CategoriaId = await CategoriaAsync(), Icone = "bi-star", Ordem = 5 }, ator);
        Assert.True(r.Ok, r.Erro);
        Assert.Equal(1, await _b.Escalar<int>($"SELECT CAST(Ativo AS int) FROM dbo.Beneficio WHERE Id={id}"));
        Assert.Equal(0, await _b.Escalar<int>($"SELECT CAST(Ficticio AS int) FROM dbo.Beneficio WHERE Id={id}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='Beneficio' AND RegistroId={id} AND Campo=N'(criado)' AND UsuarioRhId={ator}"));
    }

    [Fact]
    public async Task Beneficio_valida_nome_categoria_icone_e_duplicidade()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var cat = await CategoriaAsync();
        var (r, _, e) = await SalvarBen(new BeneficioForm(), ator);
        Assert.False(r.Ok);
        Assert.True(e.ContainsKey("Nome") && e.ContainsKey("CategoriaId"));

        (_, _, e) = await SalvarBen(new BeneficioForm { Nome = "X", CategoriaId = cat, Icone = "credit-card" }, ator);
        Assert.True(e.ContainsKey("Icone"));
        (_, _, e) = await SalvarBen(new BeneficioForm { Nome = "X", CategoriaId = cat, Icone = "bi-ok", Ordem = -1 }, ator);
        Assert.True(e.ContainsKey("Ordem"));
        (_, _, e) = await SalvarBen(new BeneficioForm { Nome = "Plano de saúde", CategoriaId = cat }, ator);   // já existe no seed
        Assert.True(e.ContainsKey("Nome"));
        (_, _, e) = await SalvarBen(new BeneficioForm { Nome = "Outro", CategoriaId = 99999 }, ator);
        Assert.True(e.ContainsKey("CategoriaId"));
    }

    [Fact]
    public async Task Editar_beneficio_grava_campo_antigo_e_novo_e_salvar_sem_mudar_nao_grava()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var cat = await CategoriaAsync();
        var nome = "Benefício edição " + Guid.NewGuid().ToString("N")[..6];
        var (_, id, _) = await SalvarBen(new BeneficioForm { Nome = nome, CategoriaId = cat, DescricaoCurta = "antiga", Icone = "bi-star", Ordem = 1 }, ator);
        var antes = await Logs("Beneficio", id!.Value);

        // mesmo conteúdo (inclusive com espaços sobrando e texto vazio no lugar de nulo): nada muda
        Assert.True((await SalvarBen(new BeneficioForm { Id = id, Nome = " " + nome + " ", CategoriaId = cat, DescricaoCurta = "antiga", Icone = "bi-star", Ordem = 1 }, ator)).R.Ok);
        Assert.Equal(antes, await Logs("Beneficio", id.Value));

        Assert.True((await SalvarBen(new BeneficioForm { Id = id, Nome = nome, CategoriaId = cat, DescricaoCurta = "nova", Icone = "bi-star", Ordem = 1 }, ator)).R.Ok);
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='Beneficio' AND RegistroId={id} AND Campo=N'DescricaoCurta' AND ValorAntigo=N'antiga' AND ValorNovo=N'nova' AND UsuarioRhId={ator}"));
    }

    [Fact]
    public async Task Beneficio_desativa_e_reativa_sem_excluir_e_audita()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        _b.Ator.UsuarioRhId = ator;
        var (_, id, _) = await SalvarBen(new BeneficioForm { Nome = "Desativável " + Guid.NewGuid().ToString("N")[..6], CategoriaId = await CategoriaAsync() }, ator);
        using (var s = _b.Escopo())
        {
            var svc = s.ServiceProvider.GetRequiredService<CadastroServico>();
            Assert.True((await svc.AlternarBeneficioAsync(id!.Value, false)).Ok);
        }
        Assert.Equal(0, await _b.Escalar<int>($"SELECT CAST(Ativo AS int) FROM dbo.Beneficio WHERE Id={id}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='Beneficio' AND RegistroId={id} AND Campo='Ativo' AND ValorAntigo=N'Sim' AND ValorNovo=N'Não'"));
        using (var s = _b.Escopo())
            Assert.True((await s.ServiceProvider.GetRequiredService<CadastroServico>().AlternarBeneficioAsync(id!.Value, true)).Ok);
        Assert.Equal(1, await _b.Escalar<int>($"SELECT CAST(Ativo AS int) FROM dbo.Beneficio WHERE Id={id}"));

        var metodos = typeof(CadastroServico).GetMethods().Select(m => m.Name);
        Assert.DoesNotContain(metodos, n => n.Contains("Excluir", StringComparison.OrdinalIgnoreCase) || n.Contains("Remover", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Programa_cria_valida_edita_e_audita()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var (r0, _, e0) = await SalvarProg(new ProgramaForm(), ator);
        Assert.False(r0.Ok);
        Assert.True(e0.ContainsKey("Nome"));
        var (_, _, e1) = await SalvarProg(new ProgramaForm { Nome = "Jovem Aprendiz (exemplo)" }, ator);   // duplicado do seed
        Assert.True(e1.ContainsKey("Nome"));
        var (_, _, e2) = await SalvarProg(new ProgramaForm { Nome = "P", Descricao = new string('a', 601) }, ator);
        Assert.True(e2.ContainsKey("Descricao"));

        var nome = "Programa " + Guid.NewGuid().ToString("N")[..6];
        var (r, id, _) = await SalvarProg(new ProgramaForm { Nome = nome, Descricao = "d1", PublicoAlvo = "todos" }, ator);
        Assert.True(r.Ok, r.Erro);
        Assert.Equal(1, await Logs("Programa", id!.Value, "(criado)"));

        var antes = await Logs("Programa", id.Value);
        Assert.True((await SalvarProg(new ProgramaForm { Id = id, Nome = nome, Descricao = "d1", PublicoAlvo = "todos" }, ator)).R.Ok);
        Assert.Equal(antes, await Logs("Programa", id.Value));   // sem mudança, sem log

        Assert.True((await SalvarProg(new ProgramaForm { Id = id, Nome = nome, Descricao = "d2", PublicoAlvo = "todos" }, ator)).R.Ok);
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='Programa' AND RegistroId={id} AND Campo=N'Descricao' AND ValorAntigo=N'd1' AND ValorNovo=N'd2'"));
    }
}
