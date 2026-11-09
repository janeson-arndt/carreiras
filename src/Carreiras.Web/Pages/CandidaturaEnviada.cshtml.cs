using Carreiras.Infra.Candidaturas;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages;

/// <summary>"Falta um passo": a candidatura só vale depois do clique no link enviado por e-mail.</summary>
public class CandidaturaEnviadaModel : PageModel
{
    private readonly ReferenciaEnvio _referencia;
    private readonly CandidaturaServico _candidaturas;
    private readonly LimitadorTentativas _limite;
    private readonly IConfiguration _config;

    public CandidaturaEnviadaModel(ReferenciaEnvio referencia, CandidaturaServico candidaturas, LimitadorTentativas limite, IConfiguration config)
    {
        _referencia = referencia; _candidaturas = candidaturas; _limite = limite; _config = config;
    }

    [BindProperty(SupportsGet = true)] public string? R { get; set; }
    public string? EmailMascarado { get; private set; }
    public string? Aviso { get; private set; }
    public bool AvisoErro { get; private set; }

    public void OnGet() => EmailMascarado = _referencia.Ler(R)?.Email;

    public async Task<IActionResult> OnPostReenviarAsync(CancellationToken ct)
    {
        var info = _referencia.Ler(R);
        EmailMascarado = info?.Email;
        if (info == null)
        {
            Aviso = "Não foi possível reenviar por aqui. Faça a candidatura de novo.";
            AvisoErro = true;
            return Page();
        }
        if (!_limite.Permitir("reenviar", HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Aviso = "Muitas tentativas em pouco tempo. Aguarde alguns minutos.";
            AvisoErro = true;
            return Page();
        }

        var r = await _candidaturas.ReenviarAsync(info.Value.Id, Link, ct);
        switch (r.Status)
        {
            case StatusEnvio.Ok:
                R = _referencia.Criar(r.ConfirmacaoId!.Value, info.Value.Email);
                Aviso = "Enviamos um novo link. O anterior deixou de valer.";
                break;
            case StatusEnvio.MuitasTentativas:
                Aviso = "Já enviamos um link há pouco. Espere um minuto e confira também o spam.";
                AvisoErro = true;
                break;
            default:
                Aviso = "Não foi possível reenviar. Faça a candidatura de novo.";
                AvisoErro = true;
                break;
        }
        return Page();
    }

    private string Link(string token)
    {
        var origem = _config["Site:Origem"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        return origem + Url.Page("/Confirmar", new { token });
    }
}
