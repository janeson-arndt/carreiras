using Carreiras.Infra.Candidaturas;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages;

/// <summary>Link do e-mail do pedido do titular. O GET só mostra o pedido; o POST executa (ver Confirmar).</summary>
public class MeusDadosConfirmarModel : PageModel
{
    private readonly TitularServico _titular;
    private readonly LimitadorTentativas _limite;

    public MeusDadosConfirmarModel(TitularServico titular, LimitadorTentativas limite) { _titular = titular; _limite = limite; }

    public ResultadoTitular Estado { get; private set; } = new(StatusConfirmacao.Invalido);
    public bool Concluido { get; private set; }
    public string? Aviso { get; private set; }

    public async Task OnGetAsync(string token, CancellationToken ct)
    {
        Estado = await _titular.ConsultarAsync(token, ct);
        if (Estado.Status == StatusConfirmacao.Invalido) Response.StatusCode = StatusCodes.Status404NotFound;
    }

    public async Task<IActionResult> OnPostAsync(string token, CancellationToken ct)
    {
        if (!_limite.Permitir("meus-dados-confirmar", HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            Estado = await _titular.ConsultarAsync(token, ct);
            Aviso = "Muitas tentativas em pouco tempo. Aguarde alguns minutos.";
            return Page();
        }
        Estado = await _titular.ConfirmarAsync(token, ct);
        Concluido = Estado.Status == StatusConfirmacao.Ok;
        if (Estado.Status == StatusConfirmacao.Invalido) Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}
