using Carreiras.Infra.Usuarios;
using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Vagas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carreiras.Testes.Integracao;

[Collection("banco")]
public class VagaEAuditoriaTestes
{
    private readonly BancoTeste _b;
    public VagaEAuditoriaTestes(BancoTeste b) => _b = b;

    private async Task<VagaForm> FormValidoAsync(string titulo)
    {
        using var s = _b.Escopo();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        return new VagaForm
        {
            Titulo = titulo, Descricao = "Descrição de teste.",
            UnidadeId = await db.Unidades.Where(u => u.Cidade == "Timbó").Select(u => u.Id).FirstAsync(),
            AreaId = await db.Areas.Select(a => a.Id).FirstAsync(),
            TurnoId = await db.Turnos.Select(a => a.Id).FirstAsync(),
            TipoContratoId = await db.TipoContratos.Select(a => a.Id).FirstAsync(),
            ModalidadeId = await db.Modalidades.Select(a => a.Id).FirstAsync(),
        };
    }

    private async Task<int> CriarAsync(VagaForm f, int ator)
    {
        _b.Ator.UsuarioRhId = ator;
        using var s = _b.Escopo();
        var (r, id, _) = await s.ServiceProvider.GetRequiredService<VagaServico>().SalvarAsync(f, ator);
        Assert.True(r.Ok, r.Erro);
        return id!.Value;
    }

    private async Task<(Resultado, int?, Dictionary<string, string>)> SalvarAsync(VagaForm f, int ator)
    {
        _b.Ator.UsuarioRhId = ator;
        using var s = _b.Escopo();
        return await s.ServiceProvider.GetRequiredService<VagaServico>().SalvarAsync(f, ator);
    }

    private Task<int> Logs(int vagaId, string? campo = null) =>
        _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='Vaga' AND RegistroId={vagaId}" +
                        (campo == null ? "" : $" AND Campo=N'{campo}'"));

    // ------------------------------------------------------------------ auditoria (RN12)

    [Fact]
    public async Task Criar_vaga_grava_log_de_criacao_com_usuario()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Analista);
        var id = await CriarAsync(await FormValidoAsync("Vaga de criação"), ator);
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='Vaga' AND RegistroId={id} AND Campo=N'(criado)' AND UsuarioRhId={ator}"));
    }

    [Fact]
    public async Task Editar_campo_grava_log_com_usuario_campo_antigo_e_novo()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Analista);
        var f = await FormValidoAsync("Título original");
        var id = await CriarAsync(f, ator);
        var o = await ObterAsync(id);
        o.Titulo = "Título alterado";
        (await SalvarAsync(o, ator)).Item1.Ok.ToString();
        Assert.Equal(1, await _b.Escalar<int>(
            $"SELECT COUNT(*) FROM dbo.LogAuditoria WHERE Tabela='Vaga' AND RegistroId={id} AND Campo=N'Titulo' " +
            $"AND ValorAntigo=N'Título original' AND ValorNovo=N'Título alterado' AND UsuarioRhId={ator}"));
    }

    [Fact]
    public async Task Salvar_sem_alterar_nada_nao_grava_log()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var f = await FormValidoAsync("Sem mudança");
        f.Requisitos = new() { new() { Texto = "Req A" }, new() { Texto = "Req B", Obrigatorio = false } };
        f.Atividades = new() { "Ativ 1" };
        var id = await CriarAsync(f, ator);
        var antes = await Logs(id);
        var r = await SalvarAsync(await ObterAsync(id), ator);
        Assert.True(r.Item1.Ok);
        Assert.Equal(antes, await Logs(id));
    }

    [Fact]
    public async Task Listas_sao_comparadas_como_conjunto_sem_considerar_ordem()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var f = await FormValidoAsync("Listas conjunto");
        f.Requisitos = new() { new() { Texto = "Req A" }, new() { Texto = "Req B" } };
        f.Diferenciais = new() { "Dif 1", "Dif 2" };
        using (var s = _b.Escopo())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            f.BeneficioIds = await db.Beneficios.OrderBy(x => x.Id).Take(3).Select(x => x.Id).ToListAsync();
        }
        var id = await CriarAsync(f, ator);
        var antes = await Logs(id);

        var o = await ObterAsync(id);
        o.Requisitos.Reverse();
        o.Diferenciais.Reverse();
        o.BeneficioIds.Reverse();
        Assert.True((await SalvarAsync(o, ator)).Item1.Ok);
        Assert.Equal(antes, await Logs(id));   // só mudou a ordem

        o = await ObterAsync(id);
        o.Requisitos.Add(new() { Texto = "Req C" });
        Assert.True((await SalvarAsync(o, ator)).Item1.Ok);
        Assert.Equal(1, await Logs(id, "Requisitos"));
        Assert.Equal("Req C", await _b.Escalar<string>($"SELECT ValorNovo FROM dbo.LogAuditoria WHERE Tabela='Vaga' AND RegistroId={id} AND Campo=N'Requisitos'"));
    }

    [Fact]
    public async Task Beneficios_alterados_geram_um_log_com_os_nomes()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var id = await CriarAsync(await FormValidoAsync("Benefícios log"), ator);
        var o = await ObterAsync(id);
        using (var s = _b.Escopo())
            o.BeneficioIds = await s.ServiceProvider.GetRequiredService<AppDbContext>().Beneficios.Where(x => x.Nome == "Plano de saúde").Select(x => x.Id).ToListAsync();
        Assert.True((await SalvarAsync(o, ator)).Item1.Ok);
        Assert.Equal("Plano de saúde", await _b.Escalar<string>($"SELECT ValorNovo FROM dbo.LogAuditoria WHERE Tabela='Vaga' AND RegistroId={id} AND Campo=N'Benefícios'"));
    }

    // ------------------------------------------------------------------ vaga (RN01, RN02, RN04)

    [Fact]
    public async Task Slug_e_unico_com_sufixo_numerico()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var a = await CriarAsync(await FormValidoAsync("Eletricista Slug"), ator);
        var b = await CriarAsync(await FormValidoAsync("Eletricista Slug"), ator);
        Assert.Equal("eletricista-slug-timbo", await Slug(a));
        Assert.Equal("eletricista-slug-timbo-2", await Slug(b));
    }

    [Fact]
    public async Task Slug_acompanha_titulo_no_rascunho_e_nao_muda_depois_de_publicada()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var id = await CriarAsync(await FormValidoAsync("Titulo um"), ator);
        var o = await ObterAsync(id);
        o.Titulo = "Titulo dois";
        await SalvarAsync(o, ator);
        Assert.Equal("titulo-dois-timbo", await Slug(id));

        using (var s = _b.Escopo()) Assert.True((await s.ServiceProvider.GetRequiredService<VagaServico>().MudarStatusAsync(id, "PUBLICADA", ator)).Ok);
        o = await ObterAsync(id);
        o.Titulo = "Titulo tres";
        await SalvarAsync(o, ator);
        Assert.Equal("titulo-dois-timbo", await Slug(id));   // RN01
    }

    [Fact]
    public async Task Mudanca_de_status_grava_historico_e_log_e_respeita_as_transicoes()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Analista);
        _b.Ator.UsuarioRhId = ator;
        var id = await CriarAsync(await FormValidoAsync("Status vaga"), ator);
        using var s = _b.Escopo();
        var svc = s.ServiceProvider.GetRequiredService<VagaServico>();

        Assert.False((await svc.MudarStatusAsync(id, "PAUSADA", ator)).Ok);      // rascunho não pausa
        Assert.True((await svc.MudarStatusAsync(id, "PUBLICADA", ator)).Ok);
        Assert.True((await svc.MudarStatusAsync(id, "PAUSADA", ator)).Ok);
        Assert.True((await svc.MudarStatusAsync(id, "ENCERRADA", ator)).Ok);
        Assert.False((await svc.MudarStatusAsync(id, "PUBLICADA", ator)).Ok);    // encerrada não reabre

        Assert.Equal(4, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.VagaStatusHist WHERE VagaId={id}"));  // rascunho + 3
        Assert.Equal(3, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.VagaStatusHist WHERE VagaId={id} AND UsuarioRhId={ator} AND Status<>'RASCUNHO'"));
        Assert.True(await Logs(id, "Status") >= 3);
    }

    [Fact]
    public async Task Vaga_encerrada_abre_bloqueada_e_duplicar_cria_rascunho_com_slug_novo()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var f = await FormValidoAsync("Para duplicar");
        f.Requisitos = new() { new() { Texto = "Req X" } };
        var id = await CriarAsync(f, ator);
        using var s = _b.Escopo();
        var svc = s.ServiceProvider.GetRequiredService<VagaServico>();
        await svc.MudarStatusAsync(id, "PUBLICADA", ator);
        await svc.MudarStatusAsync(id, "ENCERRADA", ator);

        var edit = await ObterAsync(id);
        edit.Titulo = "Tentativa";
        var r = await SalvarAsync(edit, ator);
        Assert.False(r.Item1.Ok);

        var (rd, novoId) = await svc.DuplicarAsync(id, ator);
        Assert.True(rd.Ok);
        var copia = await ObterAsync(novoId!.Value);
        Assert.Equal("RASCUNHO", await _b.Escalar<string>($"SELECT Status FROM dbo.Vaga WHERE Id={novoId}"));
        Assert.Equal("para-duplicar-timbo-2", await Slug(novoId.Value));
        Assert.Single(copia.Requisitos);
    }

    [Fact]
    public async Task Rascunho_exige_os_mesmos_campos_obrigatorios_e_valida_salario()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var vazio = new VagaForm();
        var (r, _, erros) = await SalvarAsync(vazio, ator);
        Assert.False(r.Ok);
        foreach (var campo in new[] { "Titulo", "Descricao", "UnidadeId", "AreaId", "TurnoId", "TipoContratoId", "ModalidadeId" })
            Assert.True(erros.ContainsKey(campo), campo);

        var f = await FormValidoAsync("Salário");
        f.SalarioMin = 3000; f.SalarioMax = 2000;
        Assert.True((await SalvarAsync(f, ator)).Item3.ContainsKey("SalarioMin"));
        f.SalarioMin = 2000; f.SalarioMax = null; f.ExibirSalario = true;
        Assert.True((await SalvarAsync(f, ator)).Item3.ContainsKey("SalarioMin"));
    }

    [Fact]
    public async Task Listagem_filtra_por_status()
    {
        var ator = await _b.CriarUsuarioAsync(Perfis.Administrador);
        var id = await CriarAsync(await FormValidoAsync("Filtro Lista Único"), ator);
        using var s = _b.Escopo();
        var svc = s.ServiceProvider.GetRequiredService<VagaServico>();
        var rasc = await svc.ListarAsync(new FiltroVagas("Filtro Lista Único", "RASCUNHO"));
        var pub = await svc.ListarAsync(new FiltroVagas("Filtro Lista Único", "PUBLICADA"));
        Assert.Contains(rasc, v => v.Id == id);
        Assert.DoesNotContain(pub, v => v.Id == id);
    }

    private async Task<VagaForm> ObterAsync(int id)
    {
        using var s = _b.Escopo();
        return (await s.ServiceProvider.GetRequiredService<VagaServico>().ObterAsync(id))!.Value.Form;
    }

    private Task<string> Slug(int id) => _b.Escalar<string>($"SELECT Slug FROM dbo.Vaga WHERE Id={id}");
}
