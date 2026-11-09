using System.IO.Compression;
using System.Text;
using Carreiras.Dominio.Candidaturas;

namespace Carreiras.Testes.Dominio;

/// <summary>Regras de preenchimento, tipo real do arquivo e token (Entrega 04).</summary>
public class CandidaturaDominioTestes
{
    private static DadosCandidatura Ok(Func<DadosCandidatura, DadosCandidatura>? ajusta = null)
    {
        var d = new DadosCandidatura("Maria da Silva", "maria@exemplo.com", "(47) 99999-1234", "Timbó", "SC", null, true);
        return ajusta == null ? d : ajusta(d);
    }

    public static byte[] Pdf() => Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF");

    public static byte[] Docx(bool completo = true)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string nome) { using var w = new StreamWriter(zip.CreateEntry(nome).Open()); w.Write("<x/>"); }
            Add("[Content_Types].xml");
            if (completo) Add("word/document.xml");
        }
        return ms.ToArray();
    }

    [Fact]
    public void Dados_corretos_nao_tem_erro() => Assert.Empty(ValidacaoCandidatura.Validar(Ok()));

    [Fact]
    public void Sem_aceite_do_termo_e_erro() =>
        Assert.True(ValidacaoCandidatura.Validar(Ok(d => d with { AceitouTermo = false })).ContainsKey("Aceite"));

    [Theory]
    [InlineData("Nome", "", "Nome")]
    [InlineData("Nome", "Al", "Nome")]
    [InlineData("Email", "sem-arroba", "Email")]
    [InlineData("Email", "a@b", "Email")]
    [InlineData("Email", "a b@c.com", "Email")]
    [InlineData("Telefone", "123", "Telefone")]
    [InlineData("Cidade", "", "Cidade")]
    [InlineData("Uf", "XX", "Uf")]
    [InlineData("Uf", "", "Uf")]
    public void Campo_invalido_gera_erro_no_campo(string campo, string valor, string chave)
    {
        var d = campo switch
        {
            "Nome" => Ok(x => x with { Nome = valor }),
            "Email" => Ok(x => x with { Email = valor }),
            "Telefone" => Ok(x => x with { Telefone = valor }),
            "Cidade" => Ok(x => x with { Cidade = valor }),
            _ => Ok(x => x with { Uf = valor }),
        };
        Assert.True(ValidacaoCandidatura.Validar(d).ContainsKey(chave));
    }

    [Fact]
    public void Pretensao_negativa_e_erro_e_vazia_e_aceita()
    {
        Assert.True(ValidacaoCandidatura.Validar(Ok(d => d with { PretensaoSalarial = -1 })).ContainsKey("PretensaoSalarial"));
        Assert.Empty(ValidacaoCandidatura.Validar(Ok(d => d with { PretensaoSalarial = null })));
    }

    [Fact]
    public void Email_e_normalizado_para_minusculas() =>
        Assert.Equal("maria@exemplo.com", ValidacaoCandidatura.NormalizarEmail("  Maria@Exemplo.COM "));

    [Fact]
    public void Pdf_e_docx_verdadeiros_passam()
    {
        Assert.True(ArquivoCurriculo.Validar("cv.pdf", new MemoryStream(Pdf())).Ok);
        Assert.True(ArquivoCurriculo.Validar("CV.DOCX", new MemoryStream(Docx())).Ok);
    }

    [Theory]
    [InlineData("cv.txt")]
    [InlineData("cv.exe")]
    [InlineData("cv.doc")]
    [InlineData("cv")]
    public void Extensao_fora_de_pdf_docx_e_recusada(string nome)
    {
        var r = ArquivoCurriculo.Validar(nome, new MemoryStream(Pdf()));
        Assert.False(r.Ok);
        Assert.Contains("PDF", r.Erro);
    }

    [Fact]
    public void Executavel_renomeado_para_pdf_e_recusado()
    {
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
        Assert.False(ArquivoCurriculo.Validar("cv.pdf", new MemoryStream(exe)).Ok);
    }

    [Fact]
    public void Zip_qualquer_renomeado_para_docx_e_recusado()
    {
        Assert.False(ArquivoCurriculo.Validar("cv.docx", new MemoryStream(Docx(completo: false))).Ok);
        Assert.False(ArquivoCurriculo.Validar("cv.docx", new MemoryStream(Pdf())).Ok);
    }

    [Fact]
    public void Arquivo_acima_de_5_MB_e_recusado_com_mensagem_clara()
    {
        var grande = new byte[(5 * 1024 * 1024) + 1];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(grande, 0);
        var r = ArquivoCurriculo.Validar("cv.pdf", new MemoryStream(grande));
        Assert.False(r.Ok);
        Assert.Contains("5 MB", r.Erro);
    }

    [Fact]
    public void Arquivo_de_exatamente_5_MB_passa()
    {
        var limite = new byte[5 * 1024 * 1024];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(limite, 0);
        Assert.True(ArquivoCurriculo.Validar("cv.pdf", new MemoryStream(limite)).Ok);
    }

    [Fact]
    public void Arquivo_vazio_ou_ausente_e_recusado()
    {
        Assert.False(ArquivoCurriculo.Validar("cv.pdf", new MemoryStream()).Ok);
        Assert.False(ArquivoCurriculo.Validar(null, null).Ok);
    }

    [Fact]
    public void Nome_de_exibicao_nao_carrega_pasta_nem_controle()
    {
        Assert.Equal("cv.pdf", ArquivoCurriculo.NomeParaExibicao(@"C:\Users\x\..\cv.pdf"));
        Assert.Equal("passwd.pdf", ArquivoCurriculo.NomeParaExibicao("../../etc/passwd.pdf"));
        Assert.Equal("curriculo", ArquivoCurriculo.NomeParaExibicao(""));
        Assert.True(ArquivoCurriculo.NomeParaExibicao(new string('a', 500) + ".pdf").Length <= 200);
    }

    [Fact]
    public void Token_tem_256_bits_e_o_hash_nao_revela_o_token()
    {
        var (t1, h1) = TokenConfirmacao.Gerar();
        var (t2, h2) = TokenConfirmacao.Gerar();
        Assert.NotEqual(t1, t2);
        Assert.True(t1.Length >= 43);                       // 32 bytes em base64url
        Assert.Matches("^[A-Za-z0-9_-]+$", t1);             // seguro em URL
        Assert.Equal(64, h1.Length);                        // SHA-256 em hexadecimal
        Assert.Equal(h1, TokenConfirmacao.Hash(t1));
        Assert.NotEqual(h1, h2);
        Assert.DoesNotContain(t1, h1);
    }

    [Fact]
    public void Termo_tem_versao() => Assert.False(string.IsNullOrWhiteSpace(TermoPrivacidade.VersaoAtual));
}
