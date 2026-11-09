using Carreiras.Infra.Candidaturas;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages;

/// <summary>
/// Destino do link do e-mail. O GET só MOSTRA a candidatura e um botão; quem efetiva é o POST. Assim, programas que
/// abrem links de e-mail sozinhos (antivírus, prévia) não gastam o link de uso único.
/// </summary>
public class ConfirmarModel : PageModel
{
    private readonly CandidaturaServico _candidaturas;
    private readonly LimitadorTentativas _limite;
    private readonly IConfiguration _config;

    public ConfirmarModel(CandidaturaServico candidaturas, LimitadorTentativas limite, IConfiguration config)
    {
        _candidaturas = candidaturas; _limite = limite; _config = config;
    }

    public ResultadoConfirmacao Estado { get; private set; } = new(StatusConfirmacao.Invalido);
    /// <summary>true depois que o clique no botão efetivou a candidatura.</summary>
    public bool Concluida { get; private set; }
    public string? Aviso { get; private set; }
    public bool AvisoErro { get; private set; }

    public async Task OnGetAsync(string token, CancellationToken ct)
    {
        Estado = await _candidaturas.ConsultarAsync(token, ct);
        if (Estado.Status == StatusConfirmacao.Invalido) Response.StatusCode = StatusCodes.Status404NotFound;
    }

    public async Task<IActionResult> OnPostAsync(string token, CancellationToken ct)
    {
        if (!_limite.Permitir("confirmar", HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Estado = await _candidaturas.ConsultarAsync(token, ct);
            Aviso = "Muitas tentativas em pouco tempo. Aguarde alguns minutos.";
            AvisoErro = true;
            return Page();
        }
        Estado = await _candidaturas.ConfirmarAsync(token, ct);
        Concluida = Estado.Status == StatusConfirmacao.Ok;
        if (Estado.Status == StatusConfirmacao.Invalido) Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }

    public async Task<IActionResult> OnPostNovoLinkAsync(string token, CancellationToken ct)
    {
        Estado = await _candidaturas.ConsultarAsync(token, ct);
        if (!_limite.Permitir("reenviar", HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Aviso = "Muitas tentativas em pouco tempo. Aguarde alguns minutos.";
            AvisoErro = true;
            return Page();
        }
        var r = await _candidaturas.ReenviarPorTokenAsync(token, Link, ct);
        if (r.Status == StatusEnvio.Ok) { Aviso = $"Enviamos um novo link para {Estado.EmailMascarado}."; }
        else if (r.Status == StatusEnvio.MuitasTentativas) { Aviso = "Já enviamos um link há pouco. Espere um minuto e confira o spam."; AvisoErro = true; }
        else { Aviso = "Não foi possível enviar um novo link. Faça a candidatura de novo."; AvisoErro = true; }
        return Page();
    }

    private string Link(string token)
    {
        var origem = _config["Site:Origem"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        return origem + Url.Page("/Confirmar", new { token });
    }
}
