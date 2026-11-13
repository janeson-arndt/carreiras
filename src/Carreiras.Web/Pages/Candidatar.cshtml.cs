using System.Globalization;
using Carreiras.Dominio.Candidaturas;
using Carreiras.Infra.Candidaturas;
using Carreiras.Infra.Vitrine;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace Carreiras.Web.Pages;

/// <summary>Candidatura avulsa a uma vaga publicada (Entrega 04): formulário, currículo e confirmação por e-mail.</summary>
[RequestSizeLimit(LimiteCorpo)]
[RequestFormLimits(MultipartBodyLengthLimit = LimiteCorpo)]
public class CandidatarModel : PageModel
{
    // um pouco acima dos 5 MB do currículo, para o arquivo grande chegar à validação e receber a mensagem clara
    public const long LimiteCorpo = 8L * 1024 * 1024;

    private static readonly CultureInfo Br = new("pt-BR");
    private readonly VitrineServico _vitrine;
    private readonly CandidaturaServico _candidaturas;
    private readonly DesafioHumano _desafio;
    private readonly ReferenciaEnvio _referencia;
    private readonly LimitadorTentativas _limite;
    private readonly IConfiguration _config;
    private readonly RascunhoCurriculos _rascunhos;
    private readonly ReferenciaRascunho _refRascunho;

    public CandidatarModel(VitrineServico vitrine, CandidaturaServico candidaturas, DesafioHumano desafio,
        ReferenciaEnvio referencia, LimitadorTentativas limite, IConfiguration config,
        RascunhoCurriculos rascunhos, ReferenciaRascunho refRascunho)
    {
        _vitrine = vitrine; _candidaturas = candidaturas; _desafio = desafio; _referencia = referencia; _limite = limite; _config = config;
        _rascunhos = rascunhos; _refRascunho = refRascunho;
    }

    [BindProperty(SupportsGet = true, Name = "vaga")] public string? Slug { get; set; }
    [BindProperty] public string? Nome { get; set; }
    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Telefone { get; set; }
    [BindProperty] public string? Cidade { get; set; }
    [BindProperty] public string? Uf { get; set; }
    [BindProperty] public string? PretensaoSalarial { get; set; }
    [BindProperty] public bool Aceite { get; set; }
    [BindProperty] public IFormFile? Curriculo { get; set; }
    [BindProperty] public string? Resposta { get; set; }
    [BindProperty] public string? TokenDesafio { get; set; }
    /// <summary>Referência ao currículo guardado quando o envio voltou com críticas (a pessoa não precisa anexar de novo).</summary>
    [BindProperty] public string? TokenRascunho { get; set; }
    /// <summary>Nome do arquivo guardado, para mostrar na tela.</summary>
    public string? RascunhoNome { get; private set; }
    /// <summary>Campo-isca: escondido para pessoas; robôs costumam preenchê-lo.</summary>
    [BindProperty(Name = "site")] public string? Isca { get; set; }

    /// <summary>true no banco de talentos (candidatura sem vaga).</summary>
    public virtual bool Espontanea => false;

    public VagaPublicaDetalhe? Vaga { get; private set; }
    public string Pergunta { get; private set; } = "";
    public string VersaoTermo => TermoPrivacidade.VersaoAtual;

    public static readonly string[] Ufs =
        { "AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG","PA","PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO" };

    public async Task<IActionResult> OnGetAsync()
    {
        if (!Espontanea && string.IsNullOrWhiteSpace(Slug)) return RedirectToPage("/BancoDeTalentos");
        if (!await CarregarVagaAsync()) return Page();
        NovoDesafio();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!await CarregarVagaAsync()) return Page();

        if (!_limite.Permitir("candidatar", Ip))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError("", "Muitas tentativas em pouco tempo. Aguarde alguns minutos e tente de novo.");
            NovoDesafio();
            return Page();
        }

        // robô: finge que deu certo, sem gravar nada nem mandar e-mail
        if (!string.IsNullOrWhiteSpace(Isca)) return RedirectToPage("/CandidaturaEnviada");

        decimal? pretensao = null;
        if (!string.IsNullOrWhiteSpace(PretensaoSalarial))
        {
            var txt = PretensaoSalarial.Replace("R$", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (decimal.TryParse(txt, NumberStyles.Number, Br, out var v)) pretensao = v;
            else ModelState.AddModelError(nameof(PretensaoSalarial), "Informe só números, como 3.500,00.");
        }

        if (!_desafio.Validar(TokenDesafio, Resposta))
            ModelState.AddModelError(nameof(Resposta), "A conta não confere. Tente de novo.");

        var dados = new DadosCandidatura(Nome ?? "", Email ?? "", Telefone ?? "", Cidade ?? "", Uf ?? "", pretensao, Aceite);

        // Arquivo do envio: o que veio agora ou, se a pessoa não anexou outro, o guardado no envio anterior (que voltou com críticas).
        var anterior = _refRascunho.Ler(TokenRascunho);
        Stream? fluxo = null;
        string? nomeArquivo = null;
        string? rascunhoUsado = null;
        if (Curriculo != null) { fluxo = Curriculo.OpenReadStream(); nomeArquivo = Curriculo.FileName; }
        else if (anterior is { } a && _rascunhos.Abrir(a.Arquivo) is { } guardado)
        {
            fluxo = guardado; nomeArquivo = a.NomeOriginal; rascunhoUsado = a.Arquivo;
        }

        ResultadoEnvio resultado;
        await using (fluxo)
        {
            if (ModelState.ErrorCount > 0)
            {
                // ainda valida o resto para mostrar todos os erros de uma vez
                var erros = ValidacaoCandidatura.Validar(dados);
                var arq = ArquivoCurriculo.Validar(nomeArquivo, fluxo);
                if (!arq.Ok) erros["Curriculo"] = arq.Erro!;
                foreach (var (campo, msg) in erros) if (!ModelState.ContainsKey(campo) || ModelState[campo]!.Errors.Count == 0) ModelState.AddModelError(campo, msg);
                await ManterRascunhoAsync(arq, fluxo, nomeArquivo, rascunhoUsado, anterior?.Arquivo, ct);
                NovoDesafio();
                return Page();
            }

            resultado = await _candidaturas.EnviarAsync(Espontanea ? null : Slug, dados, nomeArquivo, fluxo, Ip, LinkConfirmacao, ct);

            // envio recusado por outro motivo (e-mail já usado, erros do serviço...): o arquivo continua guardado para a próxima tentativa
            if (resultado.Status is not (StatusEnvio.Ok or StatusEnvio.VagaIndisponivel))
                await ManterRascunhoAsync(ArquivoCurriculo.Validar(nomeArquivo, fluxo), fluxo, nomeArquivo, rascunhoUsado, anterior?.Arquivo, ct);
        }

        if (resultado.Status == StatusEnvio.Ok && anterior is { } usado) _rascunhos.Apagar(usado.Arquivo);

        switch (resultado.Status)
        {
            case StatusEnvio.Ok:
                return RedirectToPage("/CandidaturaEnviada",
                    new { r = _referencia.Criar(resultado.ConfirmacaoId!.Value, CandidaturaServico.Mascarar(dados.Email.Trim().ToLowerInvariant())) });
            case StatusEnvio.Invalido:
                foreach (var (campo, msg) in resultado.Erros!) ModelState.AddModelError(campo, msg);
                break;
            case StatusEnvio.JaCandidatou:
                ModelState.AddModelError(nameof(Email), Espontanea
                    ? "Você já está no banco de talentos com este e-mail. Se quiser, candidate-se a uma vaga aberta."
                    : "Você já se candidatou a esta vaga com este e-mail. Acompanhe pelo RH ou escolha outra vaga.");
                break;
            case StatusEnvio.MuitasTentativas:
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                ModelState.AddModelError("", "Já enviamos vários links para este e-mail. Confira a caixa de entrada e o spam, ou tente de novo mais tarde.");
                break;
            case StatusEnvio.VagaIndisponivel:
                Vaga = null;
                Response.StatusCode = StatusCodes.Status404NotFound;
                return Page();
        }
        NovoDesafio();
        return Page();
    }

    /// <summary>
    /// A tela volta com críticas: o navegador não devolve o arquivo escolhido, então ele fica guardado por pouco tempo
    /// no servidor e a tela mostra o nome. Só guarda arquivo que passou na validação (tipo e tamanho).
    /// </summary>
    private async Task ManterRascunhoAsync(ResultadoArquivo arq, Stream? fluxo, string? nomeArquivo, string? rascunhoUsado, string? anterior, CancellationToken ct)
    {
        var guardado = rascunhoUsado;
        if (arq.Ok && fluxo != null && guardado == null)
            guardado = await _rascunhos.SalvarAsync(fluxo, arq.Extensao!, ct);

        // o rascunho do envio anterior some quando um arquivo novo o substitui (ou quando o arquivo não vale mais)
        if (anterior != null && anterior != guardado) _rascunhos.Apagar(anterior);

        if (guardado == null || !arq.Ok) { TokenRascunho = null; RascunhoNome = null; return; }
        RascunhoNome = ArquivoCurriculo.NomeParaExibicao(nomeArquivo);
        TokenRascunho = _refRascunho.Criar(guardado, RascunhoNome);
    }

    private string? Ip =>HttpContext.Connection.RemoteIpAddress?.ToString();

    private string LinkConfirmacao(string token)
    {
        // Origem fixa por configuração (produção): evita montar o link a partir do cabeçalho Host da requisição.
        var origem = _config["Site:Origem"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        return origem + Url.Page("/Confirmar", new { token });
    }

    protected async Task<bool> CarregarVagaAsync()
    {
        if (Espontanea) return true;
        if (!string.IsNullOrWhiteSpace(Slug)) Vaga = await _vitrine.ObterVagaAsync(Slug);
        if (Vaga == null) Response.StatusCode = StatusCodes.Status404NotFound;
        return Vaga != null;
    }

    private void NovoDesafio()
    {
        var (pergunta, token) = _desafio.Criar();
        Pergunta = pergunta;
        TokenDesafio = token;
        // a tela usa value="" explícito na resposta e o token novo no campo oculto (não os valores reenviados)
    }
}
