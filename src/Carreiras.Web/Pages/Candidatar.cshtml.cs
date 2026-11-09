using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Pages;

/// <summary>Destino do botão "Candidatar-se" até a Entrega 04 (formulário com confirmação por e-mail).</summary>
public class CandidatarModel : PageModel
{
    [BindProperty(SupportsGet = true, Name = "vaga")] public string? Slug { get; set; }
    public void OnGet() { }
}
