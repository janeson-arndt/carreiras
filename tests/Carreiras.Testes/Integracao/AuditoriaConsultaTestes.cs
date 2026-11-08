using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Auditoria;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Vagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

[Collection("banco")]
public class AuditoriaConsultaTestes
{
    private readonly BancoTeste _b;
    public AuditoriaConsultaTestes(BancoTeste b) => _b = b;

    private async Task<PaginaAuditoria> Consultar(FiltroAuditoria f, int pagina = 1, int tamanho = 25)
    {
        using var s = _b.Escopo();
        return await s.ServiceProvider.GetRequiredService<AuditoriaConsulta>().ConsultarAsync(f, pagina, tamanho);
    }

    private async Task<(int vagaId, int ator)> VagaCriadaPorAsync(string titulo)
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        _b.Ator.UsuarioRhId = ator;
        using var s = _b.Escopo();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var f = new VagaForm
        {
            Titulo = titulo, Descricao = "d",
            UnidadeId = await db.Unidades.Select(u => u.Id).OrderBy(i => i).FirstAsync(),
            AreaId = await db.Areas.Select(a => a.Id).OrderBy(i => i).FirstAsync(),
            TurnoId = await db.Turnos.Select(a => a.Id).FirstAsync(),
            TipoContratoId = await db.TipoContratos.Select(a => a.Id).FirstAsync(),
            ModalidadeId = await db.Modalidades.Select(a => a.Id).FirstAsync(),
        };
        var (r, id, _) = await s.ServiceProvider.GetRequiredService<VagaServico>().SalvarAsync(f, ator);
        Assert.True(r.Ok, r.Erro);
        return (id!.Value, ator);
    }

    [Fact]
    public async Task Mostra_o_nome_de_quem_alterou_e_o_titulo_do_registro()
    {
        var titulo = "Auditoria rótulo " + Guid.NewGuid().ToString("N")[..6];
        var (id, ator) = await VagaCriadaPorAsync(titulo);
        var p = await Consultar(new FiltroAuditoria("Vaga", ator, id));
        var linha = Assert.Single(p.Itens);
        Assert.Equal(titulo, linha.Registro);
        Assert.Equal("Teste ADMINISTRADOR", linha.Usuario);
        Assert.Equal("(criado)", linha.Campo);
    }

    [Fact]
    public async Task Filtra_por_tipo_usuario_registro_e_campo_e_mais_recentes_primeiro()
    {
        var (id, ator) = await VagaCriadaPorAsync("Auditoria filtros " + Guid.NewGuid().ToString("N")[..6]);
        using (var s = _b.Escopo())
        {
            var svc = s.ServiceProvider.GetRequiredService<VagaServico>();
            await svc.MudarStatusAsync(id, "PUBLICADA", ator);
            await svc.MudarStatusAsync(id, "PAUSADA", ator);
        }
        var todas = await Consultar(new FiltroAuditoria(RegistroId: id, Tabela: "Vaga"));
        Assert.True(todas.Total >= 3);
        Assert.Equal(todas.Itens.OrderByDescending(i => i.DataHoraUtc).ThenByDescending(i => i.Id).Select(i => i.Id), todas.Itens.Select(i => i.Id));

        var soStatus = await Consultar(new FiltroAuditoria("Vaga", ator, id, Campo: "Status"));
        Assert.NotEmpty(soStatus.Itens);
        Assert.All(soStatus.Itens, i => Assert.Equal("Status", i.Campo));

        var outroTipo = await Consultar(new FiltroAuditoria("Programa", ator, id));
        Assert.Empty(outroTipo.Itens);
        var outroUsuario = await Consultar(new FiltroAuditoria("Vaga", ator + 100000, id));
        Assert.Empty(outroUsuario.Itens);
    }

    [Fact]
    public async Task Troca_ids_de_dominio_pelo_nome()
    {
        var (id, ator) = await VagaCriadaPorAsync("Auditoria domínio " + Guid.NewGuid().ToString("N")[..6]);
        int novaArea, areaAntiga;
        using (var s = _b.Escopo())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            var o = (await s.ServiceProvider.GetRequiredService<VagaServico>().ObterAsync(id))!.Value.Form;
            areaAntiga = o.AreaId;
            novaArea = await db.Areas.Where(a => a.Id != areaAntiga).Select(a => a.Id).FirstAsync();
            o.AreaId = novaArea;
            Assert.True((await s.ServiceProvider.GetRequiredService<VagaServico>().SalvarAsync(o, ator)).Resultado.Ok);
        }
        var nomeAntigo = await _b.Escalar<string>($"SELECT Nome FROM dbo.Area WHERE Id={areaAntiga}");
        var nomeNovo = await _b.Escalar<string>($"SELECT Nome FROM dbo.Area WHERE Id={novaArea}");
        var linha = Assert.Single((await Consultar(new FiltroAuditoria("Vaga", ator, id, Campo: "AreaId"))).Itens);
        Assert.Equal(nomeAntigo, linha.ValorAntigo);
        Assert.Equal(nomeNovo, linha.ValorNovo);
    }

    [Fact]
    public async Task Filtra_por_periodo_na_data_local()
    {
        var (id, ator) = await VagaCriadaPorAsync("Auditoria período " + Guid.NewGuid().ToString("N")[..6]);
        var hoje = DateOnly.FromDateTime(DateTime.Now);
        Assert.NotEmpty((await Consultar(new FiltroAuditoria("Vaga", ator, id, De: hoje, Ate: hoje))).Itens);
        Assert.Empty((await Consultar(new FiltroAuditoria("Vaga", ator, id, De: hoje.AddDays(1)))).Itens);
        Assert.Empty((await Consultar(new FiltroAuditoria("Vaga", ator, id, Ate: hoje.AddDays(-1)))).Itens);
    }

    [Fact]
    public async Task Pagina_os_resultados_e_informa_o_total()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var registro = 900000 + Random.Shared.Next(99999);
        for (var i = 0; i < 7; i++)
            await BancoTeste.Executar(_b.ConexaoAdmin,
                $"INSERT dbo.LogAuditoria (UsuarioRhId, Tabela, RegistroId, Campo, ValorAntigo, ValorNovo) VALUES ({ator}, N'Programa', {registro}, N'Campo{i}', N'a', N'b')");
        var p1 = await Consultar(new FiltroAuditoria("Programa", ator, registro), 1, 5);
        var p2 = await Consultar(new FiltroAuditoria("Programa", ator, registro), 2, 5);
        Assert.Equal(7, p1.Total);
        Assert.Equal(2, p1.TotalPaginas);
        Assert.Equal(5, p1.Itens.Count);
        Assert.Equal(2, p2.Itens.Count);
        Assert.Empty(p1.Itens.Select(i => i.Id).Intersect(p2.Itens.Select(i => i.Id)));
        Assert.Equal($"#{registro}", p1.Itens[0].Registro);   // registro inexistente: mostra o número
    }

    [Fact]
    public async Task Entrada_com_aspas_nao_quebra_a_consulta()
    {
        var p = await Consultar(new FiltroAuditoria(Tabela: "x'; DROP TABLE dbo.LogAuditoria;--", Campo: "'; --"));
        Assert.Equal(0, p.Total);
        Assert.True(await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.LogAuditoria") > 0);
    }
}
