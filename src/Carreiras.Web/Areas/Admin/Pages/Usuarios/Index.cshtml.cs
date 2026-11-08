using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Usuarios;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Carreiras.Web.Areas.Admin.Pages.Usuarios;

[Authorize(Policy = Politicas.SomenteAdministrador)]
public class IndexModel : PageModel
{
    private readonly GestaoUsuarios _gestao;
    public IndexModel(GestaoUsuarios gestao) => _gestao = gestao;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Situacao { get; set; }

    public List<UsuarioItem> Itens { get; private set; } = new();
    public int AtorId => User.UsuarioRhId();
    private List<UsuarioItem> _todos = new();

    public async Task OnGetAsync()
    {
        _todos = await _gestao.ListarAsync();
        IEnumerable<UsuarioItem> q = _todos;
        if (!string.IsNullOrWhiteSpace(Q))
            q = q.Where(u => u.Nome.Contains(Q, StringComparison.CurrentCultureIgnoreCase)
                          || u.Email.Contains(Q, StringComparison.CurrentCultureIgnoreCase));
        q = Situacao switch
        {
            "ativo" => q.Where(u => u.Situacao == SituacaoUsuario.Ativo),
            "pendente" => q.Where(u => u.Situacao == SituacaoUsuario.ConvitePendente),
            "desativado" => q.Where(u => u.Situacao == SituacaoUsuario.Desativado),
            _ => q,
        };
        Itens = q.ToList();
    }

    public string? MotivoDesativar(UsuarioItem u) => GestaoUsuarios.MotivoBloqueioDesativar(u, AtorId, _todos);

    public Task<IActionResult> OnPostDesativarAsync(int id) =>
        Executar(() => _gestao.DesativarAsync(id, AtorId), "Usuário desativado. As sessões abertas dele foram encerradas.");

    public Task<IActionResult> OnPostReativarAsync(int id) =>
        Executar(() => _gestao.ReativarAsync(id, AtorId), "Usuário reativado.");

    public Task<IActionResult> OnPostReenviarConviteAsync(int id) =>
        Executar(() => _gestao.ReenviarConviteAsync(id, AtorId, LinksConvite.Criar(Url, Request)), "Convite reenviado.");

    public Task<IActionResult> OnPostNovaSenhaAsync(int id) =>
        Executar(() => _gestao.EnviarNovaSenhaAsync(id, AtorId, LinksConvite.Criar(Url, Request)), "Link de nova senha enviado por e-mail.");

    private async Task<IActionResult> Executar(Func<Task<Resultado>> acao, string ok)
    {
        var r = await acao();
        if (r.Ok) TempData["Aviso"] = ok; else TempData["Erro"] = r.Erro;
        return RedirectToPage();
    }
}
