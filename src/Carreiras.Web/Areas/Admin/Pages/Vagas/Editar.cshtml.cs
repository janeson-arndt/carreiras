using Carreiras.Dominio.Usuarios;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Vagas;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Web.Areas.Admin.Pages.Vagas;

public class EditarModel : PageModel
{
    private readonly VagaServico _vagas;
    private readonly AppDbContext _db;
    public EditarModel(VagaServico vagas, AppDbContext db) { _vagas = vagas; _db = db; }

    [BindProperty] public VagaForm Form { get; set; } = new();
    // Requisitos em duas caixas (uma linha por item), para ficar simples e acessível.
    [BindProperty] public string RequisitosObrigatorios { get; set; } = "";
    [BindProperty] public string RequisitosDesejaveis { get; set; } = "";
    [BindProperty] public string AtividadesTexto { get; set; } = "";
    [BindProperty] public string DiferenciaisTexto { get; set; } = "";

    public string Status { get; private set; } = "RASCUNHO";
    public Dictionary<string, string> Erros { get; private set; } = new();
    public string? ErroGeral { get; private set; }

    public List<Unidade> Unidades { get; private set; } = new();
    public List<Area> Areas { get; private set; } = new();
    public List<Turno> Turnos { get; private set; } = new();
    public List<TipoContrato> Contratos { get; private set; } = new();
    public List<Modalidade> Modalidades { get; private set; } = new();
    public List<Programa> Programas { get; private set; } = new();
    public List<BeneficioCategoria> Categorias { get; private set; } = new();

    public bool PodeGerenciar => User.IsInRole(Perfis.Administrador) || User.IsInRole(Perfis.Analista);
    public bool Bloqueada => !PodeGerenciar || Status == "ENCERRADA";
    public bool Nova => Form.Id is null;

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            var o = await _vagas.ObterAsync(id.Value);
            if (o == null) return NotFound();
            (Form, Status) = o.Value;
            RequisitosObrigatorios = string.Join("\n", Form.Requisitos.Where(r => r.Obrigatorio).Select(r => r.Texto));
            RequisitosDesejaveis = string.Join("\n", Form.Requisitos.Where(r => !r.Obrigatorio).Select(r => r.Texto));
            AtividadesTexto = string.Join("\n", Form.Atividades);
            DiferenciaisTexto = string.Join("\n", Form.Diferenciais);
        }
        await CarregarListasAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        if (!PodeGerenciar) return Forbid();
        Form.Id = id;
        Form.Requisitos = Linhas(RequisitosObrigatorios).Select(t => new RequisitoForm { Texto = t, Obrigatorio = true })
            .Concat(Linhas(RequisitosDesejaveis).Select(t => new RequisitoForm { Texto = t, Obrigatorio = false })).ToList();
        Form.Atividades = Linhas(AtividadesTexto);
        Form.Diferenciais = Linhas(DiferenciaisTexto);
        Form.BeneficioIds ??= new();

        var (r, novoId, erros) = await _vagas.SalvarAsync(Form, User.UsuarioRhId());
        if (r.Ok)
        {
            TempData["Aviso"] = id is null ? "Vaga criada como rascunho." : "Vaga salva.";
            return RedirectToPage("Editar", new { id = novoId });
        }
        Erros = erros;
        ErroGeral = r.Erro;
        if (id is not null) Status = (await _vagas.ObterAsync(id.Value))?.Status ?? Status;
        await CarregarListasAsync();
        return Page();
    }

    private static List<string> Linhas(string? t) =>
        (t ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    private async Task CarregarListasAsync()
    {
        Unidades = await _db.Unidades.AsNoTracking().Where(x => x.Ativo).OrderBy(x => x.Nome).ToListAsync();
        Areas = await _db.Areas.AsNoTracking().Where(x => x.Ativo).OrderBy(x => x.Nome).ToListAsync();
        Turnos = await _db.Turnos.AsNoTracking().Where(x => x.Ativo).OrderBy(x => x.Id).ToListAsync();
        Contratos = await _db.TipoContratos.AsNoTracking().Where(x => x.Ativo).OrderBy(x => x.Id).ToListAsync();
        Modalidades = await _db.Modalidades.AsNoTracking().Where(x => x.Ativo).OrderBy(x => x.Id).ToListAsync();
        Programas = await _db.Programas.AsNoTracking().Where(x => x.Ativo).OrderBy(x => x.Nome).ToListAsync();
        Categorias = await _db.BeneficioCategoria.AsNoTracking().Include(c => c.Beneficios.Where(b => b.Ativo).OrderBy(b => b.Ordem))
            .OrderBy(c => c.Ordem).ToListAsync();
    }

    public string Erro(string campo) => Erros.TryGetValue(campo, out var m) ? m : "";
    public string Invalido(string campo) => Erros.ContainsKey(campo) ? "true" : "false";
}
