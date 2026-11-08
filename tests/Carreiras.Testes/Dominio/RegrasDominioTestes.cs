using Carreiras.Dominio.Usuarios;
using Carreiras.Dominio.Vagas;

namespace Carreiras.Testes.Dominio;

public class StatusVagaTestes
{
    [Theory]
    [InlineData("RASCUNHO", "PUBLICADA")]
    [InlineData("PUBLICADA", "PAUSADA")]
    [InlineData("PAUSADA", "PUBLICADA")]
    [InlineData("PUBLICADA", "ENCERRADA")]
    [InlineData("PAUSADA", "ENCERRADA")]
    public void Transicoes_permitidas(string de, string para) => Assert.True(StatusVaga.PodeTransitar(de, para));

    [Theory]
    [InlineData("RASCUNHO", "PAUSADA")]
    [InlineData("RASCUNHO", "ENCERRADA")]
    [InlineData("PUBLICADA", "RASCUNHO")]
    [InlineData("ENCERRADA", "PUBLICADA")]
    [InlineData("ENCERRADA", "RASCUNHO")]
    [InlineData("PUBLICADA", "PUBLICADA")]
    public void Transicoes_proibidas(string de, string para) => Assert.False(StatusVaga.PodeTransitar(de, para));

    [Fact]
    public void Encerrada_nao_tem_destino() => Assert.Empty(StatusVaga.Destinos("ENCERRADA"));
}

public class SlugVagaTestes
{
    [Theory]
    [InlineData("Operador(a) de Produção", "Timbó", "operador-a-de-producao-timbo")]
    [InlineData("  Técnico de Metalurgia - Laboratório ", "Timbó", "tecnico-de-metalurgia-laboratorio-timbo")]
    [InlineData("Analista Comercial", "Canoas", "analista-comercial-canoas")]
    public void Base_normaliza_acentos_e_simbolos(string titulo, string cidade, string esperado) =>
        Assert.Equal(esperado, SlugVaga.Base(titulo, cidade));

    [Fact]
    public void Unico_acrescenta_sufixo_numerico()
    {
        var usados = new HashSet<string> { "soldador-timbo", "soldador-timbo-2" };
        Assert.Equal("soldador-timbo-3", SlugVaga.Unico("soldador-timbo", usados.Contains));
        Assert.Equal("outro-timbo", SlugVaga.Unico("outro-timbo", usados.Contains));
    }

    [Fact]
    public void Unico_respeita_limite_do_banco()
    {
        var longo = new string('a', 400);
        Assert.True(SlugVaga.Unico(longo, _ => false).Length <= 150);
    }
}

public class RegrasSalarioTestes
{
    [Fact] public void Sem_valores_e_sem_exibir_e_valido() => Assert.Null(RegrasSalario.Validar(null, null, false));
    [Fact] public void Faixa_correta_e_valida() => Assert.Null(RegrasSalario.Validar(2000, 3000, true));
    [Fact] public void Maximo_menor_que_minimo_e_invalido() => Assert.NotNull(RegrasSalario.Validar(3000, 2000, false));
    [Fact] public void Exibir_exige_os_dois_valores() => Assert.NotNull(RegrasSalario.Validar(2000, null, true));
    [Fact] public void Exibir_sem_nenhum_valor_e_invalido() => Assert.NotNull(RegrasSalario.Validar(null, null, true));
    [Fact] public void Valor_negativo_e_invalido() => Assert.NotNull(RegrasSalario.Validar(-1, 10, false));
}

public class RegrasUsuarioTestes
{
    [Fact]
    public void Ultimo_administrador_que_entra_nao_pode_ser_perdido() =>
        Assert.True(RegrasUsuario.PerderiaUltimoAdministrador(Perfis.Administrador, true, 0));

    [Fact]
    public void Com_outro_administrador_que_entra_pode() =>
        Assert.False(RegrasUsuario.PerderiaUltimoAdministrador(Perfis.Administrador, true, 1));

    [Fact]
    public void Administrador_que_nao_entra_nao_conta_como_ultimo() =>
        Assert.False(RegrasUsuario.PerderiaUltimoAdministrador(Perfis.Administrador, false, 0));

    [Fact]
    public void Nao_administrador_nunca_e_o_ultimo() =>
        Assert.False(RegrasUsuario.PerderiaUltimoAdministrador(Perfis.Analista, true, 0));

    [Theory]
    [InlineData(true, true, SituacaoUsuario.Ativo)]
    [InlineData(true, false, SituacaoUsuario.ConvitePendente)]
    [InlineData(false, true, SituacaoUsuario.Desativado)]
    [InlineData(false, false, SituacaoUsuario.Desativado)]
    public void Situacao(bool ativo, bool aceito, SituacaoUsuario esperado) =>
        Assert.Equal(esperado, RegrasUsuario.Situacao(ativo, aceito));

    [Fact] public void Proprio() => Assert.True(RegrasUsuario.EhProprio(3, 3));
}
