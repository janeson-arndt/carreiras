using Carreiras.Dominio.Candidaturas;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages;

public class TermoPrivacidadeModel : PageModel
{
    private readonly IConfiguration _config;
    public TermoPrivacidadeModel(IConfiguration config) => _config = config;

    public string Versao => TermoPrivacidade.VersaoAtual;
    public string EmailEncarregado => _config["Privacidade:EmailEncarregado"] ?? "dpo@exemplo.local";
    /// <summary>Enquanto o jurídico não revisar o texto, a página avisa que é um texto-base.</summary>
    public bool RevisadoPeloJuridico => _config.GetValue<bool>("Privacidade:RevisadoPeloJuridico");

    public void OnGet() { }
}
