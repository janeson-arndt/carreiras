using Carreiras.Dominio.Candidaturas;
using Carreiras.Infra.Candidaturas;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages;

/// <summary>"Meus dados": o candidato pede cópia ou exclusão pelo e-mail; só depois do clique no link o pedido é executado.</summary>
public class MeusDadosModel : PageModel
{
    private readonly TitularServico _titular;
    private readonly DesafioHumano _desafio;
    private readonly LimitadorTentativas _limite;
    private readonly IConfiguration _config;

    public MeusDadosModel(TitularServico titular, DesafioHumano desafio, LimitadorTentativas limite, IConfiguration config)
    {
        _titular = titular; _desafio = desafio; _limite = limite; _config = config;
    }

    [BindProperty] public string? Email { get; set; }
    [BindProperty] public string? Tipo { get; set; }
    [BindProperty] public string? Resposta { get; set; }
    [BindProperty] public string? TokenDesafio { get; set; }

    public string Pergunta { get; private set; } = "";
    public bool Enviado { get; private set; }

    public void OnGet() => NovoDesafio();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!_limite.Permitir("meus-dados", HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError("", "Muitas tentativas em pouco tempo. Aguarde alguns minutos e tente de novo.");
            NovoDesafio();
            return Page();
        }

        var mail = ValidacaoCandidatura.NormalizarEmail(Email);
        if (!ValidacaoCandidatura.EmailValido(mail)) ModelState.AddModelError(nameof(Email), "Informe um e-mail válido, como nome@exemplo.com.");
        if (Tipo is not (TitularServico.Copia or TitularServico.Exclusao)) ModelState.AddModelError(nameof(Tipo), "Escolha o que você quer pedir.");
        if (!_desafio.Validar(TokenDesafio, Resposta)) ModelState.AddModelError(nameof(Resposta), "A conta não confere. Tente de novo.");
        if (ModelState.ErrorCount > 0) { NovoDesafio(); return Page(); }

        await _titular.AbrirAsync(mail, Tipo!, Link, ct);
        Enviado = true;   // mesma resposta exista ou não o e-mail na base
        return Page();
    }

    private string Link(string token)
    {
        var origem = _config["Site:Origem"]?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        return origem + Url.Page("/MeusDadosConfirmar", new { token });
    }

    private void NovoDesafio()
    {
        var (p, t) = _desafio.Criar();
        Pergunta = p; TokenDesafio = t;
    }
}
