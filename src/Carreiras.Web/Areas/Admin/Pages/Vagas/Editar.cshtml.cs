using Carreiras.Dominio.Usuarios;
using Carreiras.Dominio.Vagas;
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
    private static readonly Dictionary<string, string> MensagemStatus = new()
    {
        [StatusVaga.Publicada] = "Vaga publicada.", [StatusVaga.Pausada] = "Vaga pausada.", [StatusVaga.Encerrada] = "Vaga encerrada.",
    };

    private readonly VagaServico _vagas;
    private readonly AppDbContext _db;
    public EditarModel(VagaServico vagas, AppDbContext db) { _vagas = vagas; _db = db; }

    [BindProperty] public VagaForm Form { get; set; } = new();
    // Listas em caixas de texto (um item por linha), para ficar simples e acessível.
    [BindProperty] public string RequisitosObrigatorios { get; set; } = "";
    [BindProperty] public string RequisitosDesejaveis { get; set; } = "";
    [BindProperty] public string AtividadesTexto { get; set; } = "";
    [BindProperty] public string DiferenciaisTexto { get; set; } = "";

    public string Status { get; private set; } = StatusVaga.Rascunho;
    public VagaResumo? Resumo { get; private set; }
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
    public bool Encerrada => Status == StatusVaga.Encerrada;
    public bool Bloqueada => !PodeGerenciar || Encerrada;
    public bool Nova => Form.Id is null;
    public IReadOnlyList<string> Transicoes => Nova ? StatusVaga.Destinos(StatusVaga.Rascunho) : StatusVaga.Destinos(Status);

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
            Resumo = await _vagas.ResumoAsync(id.Value);
        }
        await CarregarListasAsync();
        return Page();
    }

    public Task<IActionResult> OnPostAsync(int? id) => SalvarAsync(id, null);

    /// <summary>"Salvar e publicar/pausar/encerrar": grava o formulário e, se estiver válido, muda o status (RN02).</summary>
    public Task<IActionResult> OnPostTransicaoAsync(int? id, string novo) => SalvarAsync(id, novo);

    private async Task<IActionResult> SalvarAsync(int? id, string? novoStatus)
    {
        if (!PodeGerenciar) return Forbid();
        Form.Id = id;
        Form.Requisitos = Linhas(RequisitosObrigatorios).Select(t => new RequisitoForm { Texto = t, Obrigatorio = true })
            .Concat(Linhas(RequisitosDesejaveis).Select(t => new RequisitoForm { Texto = t, Obrigatorio = false })).ToList();
        Form.Atividades = Linhas(AtividadesTexto);
        Form.Diferenciais = Linhas(DiferenciaisTexto);
        Form.BeneficioIds ??= new();

        var (r, vagaId, erros) = await _vagas.SalvarAsync(Form, User.UsuarioRhId());
        if (r.Ok && novoStatus != null)
        {
            var t = await _vagas.MudarStatusAsync(vagaId!.Value, novoStatus, User.UsuarioRhId());
            TempData[t.Ok ? "Aviso" : "Erro"] = t.Ok ? MensagemStatus.GetValueOrDefault(novoStatus, "Situação da vaga atualizada.") : t.Erro;
            return RedirectToPage("Editar", new { id = vagaId });
        }
        if (r.Ok)
        {
            TempData["Aviso"] = id is null ? "Vaga criada como rascunho." : "Vaga salva.";
            return RedirectToPage("Editar", new { id = vagaId });
        }

        Erros = erros;
        ErroGeral = r.Erro;
        if (id is not null)
        {
            Status = (await _vagas.ObterAsync(id.Value))?.Status ?? Status;
            Resumo = await _vagas.ResumoAsync(id.Value);
        }
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

    public static string RotuloStatus(string s) => s switch
    {
        StatusVaga.Rascunho => "Rascunho", StatusVaga.Publicada => "Publicada", StatusVaga.Pausada => "Pausada", _ => "Encerrada",
    };

    public static string RotuloTransicao(string atual, string para) => para switch
    {
        StatusVaga.Publicada => atual == StatusVaga.Pausada ? "Reativar" : "Publicar",
        StatusVaga.Pausada => "Pausar",
        _ => "Encerrar",
    };
}
