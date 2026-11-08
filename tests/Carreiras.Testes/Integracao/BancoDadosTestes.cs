using Microsoft.Data.SqlClient;

namespace Carreiras.Testes.Integracao;

[Collection("banco")]
public class BancoDadosTestes
{
    private readonly BancoTeste _b;
    public BancoDadosTestes(BancoTeste b) => _b = b;

    [Fact]
    public async Task Seed_cria_os_totais_esperados()
    {
        Assert.Equal(26, await _b.Escalar<int>("SELECT COUNT(*) FROM sys.tables WHERE name NOT LIKE 'AspNet%' AND name NOT LIKE '__EF%'"));
        Assert.Equal(3, await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.Unidade"));
        Assert.Equal(13, await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.Beneficio WHERE Ficticio = 1"));
        Assert.Equal(3, await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.Programa WHERE Ficticio = 1"));
        Assert.Equal(5, await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.ConteudoInstitucional WHERE Secao IN ('sobre','qualidade','meio-ambiente','diversidade','privacidade')"));
        Assert.Equal(6, await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.Vaga WHERE Status='PUBLICADA' AND Slug IN ('operador-de-producao-timbo','soldador-timbo','mecanico-de-manutencao-industrial-timbo','tecnico-de-metalurgia-laboratorio-timbo','auxiliar-de-expedicao-recife','estagio-engenharia-de-materiais-timbo')"));
        Assert.Equal(1, await _b.Escalar<int>("SELECT COUNT(*) FROM dbo.Vaga WHERE Status='RASCUNHO' AND Slug='analista-comercial-canoas'"));
    }

    [Fact]
    public async Task Banco_rejeita_segunda_candidatura_do_mesmo_candidato_a_mesma_vaga()
    {
        var (cand, vaga, cur) = await Preparar("dup");
        await Executar($"INSERT dbo.Candidatura (CandidatoId, VagaId, CurriculoId) VALUES ({cand},{vaga},{cur})");
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            Executar($"INSERT dbo.Candidatura (CandidatoId, VagaId, CurriculoId) VALUES ({cand},{vaga},{cur})"));
        Assert.Contains("UX_Candidatura_Candidato_Vaga", ex.Message);

        // espontâneas (VagaId nulo) podem se repetir
        await Executar($"INSERT dbo.Candidatura (CandidatoId, VagaId, CurriculoId) VALUES ({cand},NULL,{cur})");
        await Executar($"INSERT dbo.Candidatura (CandidatoId, VagaId, CurriculoId) VALUES ({cand},NULL,{cur})");
    }

    [Fact]
    public async Task Rotina_de_anonimizacao_troca_o_email_e_mantem_as_candidaturas()
    {
        var (cand, vaga, cur) = await Preparar("anon");
        await Executar($"INSERT dbo.Candidatura (CandidatoId, VagaId, CurriculoId) VALUES ({cand},{vaga},{cur})");
        await Executar($"INSERT dbo.ConsentimentoLgpd (CandidatoId, VersaoTermo, Ip) VALUES ({cand},'1.0','10.0.0.1')");
        await Executar($"UPDATE dbo.Candidato SET RetencaoAte = DATEADD(DAY,-1,SYSUTCDATETIME()) WHERE Id={cand}");

        await Executar("EXEC dbo.usp_AnonimizarCandidatosExpirados");

        Assert.Equal($"anonimizado-{cand}@invalid.local", await _b.Escalar<string>($"SELECT Email FROM dbo.Candidato WHERE Id={cand}"));
        Assert.Equal("Anonimizado", await _b.Escalar<string>($"SELECT Nome FROM dbo.Candidato WHERE Id={cand}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.Candidatura WHERE CandidatoId={cand}"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.Curriculo WHERE CandidatoId={cand} AND RemovidoEm IS NOT NULL"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.ConsentimentoLgpd WHERE CandidatoId={cand} AND Ip IS NULL"));
        Assert.Equal(1, await _b.Escalar<int>($"SELECT COUNT(*) FROM dbo.Candidato WHERE Id={cand} AND AnonimizadoEm IS NOT NULL"));
    }

    private async Task<(int cand, int vaga, int cur)> Preparar(string tag)
    {
        var email = $"{tag}-{Guid.NewGuid():N}@teste.local";
        await Executar($"INSERT dbo.Candidato (Email, Nome) VALUES (N'{email}', N'Candidato {tag}')");
        var cand = await _b.Escalar<int>($"SELECT Id FROM dbo.Candidato WHERE Email=N'{email}'");
        await Executar($"INSERT dbo.Curriculo (CandidatoId, NomeArquivo, Caminho, TamanhoKb) VALUES ({cand}, N'cv.pdf', N'x/cv.pdf', 10)");
        var cur = await _b.Escalar<int>($"SELECT MAX(Id) FROM dbo.Curriculo WHERE CandidatoId={cand}");
        var vaga = await _b.Escalar<int>("SELECT TOP 1 Id FROM dbo.Vaga ORDER BY Id");
        return (cand, vaga, cur);
    }

    private Task Executar(string sql) => BancoTeste.Executar(_b.ConexaoAdmin, sql);
}
