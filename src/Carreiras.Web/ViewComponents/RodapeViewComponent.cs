using Carreiras.Infra.Vitrine;
using Microsoft.AspNetCore.Mvc;

namespace Carreiras.Web.ViewComponents;

public sealed class RodapeModelo
{
    public List<UnidadePublica> Unidades { get; init; } = new();
    public bool Aviso { get; init; }
}

/// <summary>Rodapé do portal público: logo, redes sociais e o endereço de cada unidade (vindos do cadastro).</summary>
public sealed class RodapeViewComponent : ViewComponent
{
    private readonly VitrineServico _vitrine;
    public RodapeViewComponent(VitrineServico vitrine) => _vitrine = vitrine;

    public async Task<IViewComponentResult> InvokeAsync(bool aviso) =>
        View(new RodapeModelo { Unidades = await _vitrine.UnidadesAsync(), Aviso = aviso });
}
