using System.Globalization;
using Carreiras.Infra.Dados.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Carreiras.Infra.Auditoria;

/// <summary>
/// RN12: ponto único de auditoria. Grava LogAuditoria para as tabelas auditadas.
/// Não use ExecuteUpdate/ExecuteDelete nessas tabelas: eles não passam por aqui.
/// Listas (requisitos, atividades, diferenciais, benefícios) são comparadas como conjunto.
/// Nunca registra senha, hash ou token.
/// </summary>
public sealed class AuditoriaInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<Type> Auditadas = new()
    {
        typeof(Vaga), typeof(Beneficio), typeof(Programa), typeof(ConteudoInstitucional), typeof(UsuarioRh),
    };

    // Campos que nunca entram no log (credenciais, ruído de sessão).
    private static readonly HashSet<string> Ignorados = new(StringComparer.OrdinalIgnoreCase)
    {
        "SenhaHash", "IdentityUserId", "UltimoAcessoEm",
    };

    private readonly IUsuarioAuditoria _usuario;
    private readonly List<(object Entidade, string Tabela)> _criadasPendentes = new();
    private bool _gravandoLogs;

    public AuditoriaInterceptor(IUsuarioAuditoria usuario) => _usuario = usuario;

    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> r)
    {
        Coletar(e.Context!, null).GetAwaiter().GetResult();
        return r;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default)
    {
        await Coletar(e.Context!, ct);
        return r;
    }

    public override int SavedChanges(SaveChangesCompletedEventData e, int result)
    {
        GravarCriacoes(e.Context!, null).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
    {
        await GravarCriacoes(e.Context!, ct);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData e) => _criadasPendentes.Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData e, CancellationToken ct = default)
    {
        _criadasPendentes.Clear();
        return Task.CompletedTask;
    }

    private async Task Coletar(DbContext ctx, CancellationToken? ct)
    {
        if (_gravandoLogs) return;
        ctx.ChangeTracker.DetectChanges();
        var logs = new List<LogAuditoria>();
        var listas = new Dictionary<(int VagaId, string Lista), (List<string> Antes, List<string> Depois)>();
        var beneficios = new Dictionary<int, (HashSet<int> Antes, HashSet<int> Depois)>();

        foreach (var entry in ctx.ChangeTracker.Entries().ToList())
        {
            var tipo = entry.Entity.GetType();

            if (Auditadas.Contains(tipo))
            {
                var tabela = entry.Metadata.GetTableName()!;
                if (entry.State == EntityState.Added) _criadasPendentes.Add((entry.Entity, tabela));
                else if (entry.State == EntityState.Deleted)
                    logs.Add(Novo(tabela, ChaveId(entry), "(excluído)", null, null));
                else if (entry.State == EntityState.Modified)
                    foreach (var p in entry.Properties)
                    {
                        if (!p.IsModified || p.Metadata.IsPrimaryKey() || Ignorados.Contains(p.Metadata.Name)) continue;
                        var antigo = Texto(p.OriginalValue);
                        var novo = Texto(p.CurrentValue);
                        if (antigo == novo) continue;   // salvar sem mudar nada não grava log
                        logs.Add(Novo(tabela, ChaveId(entry), p.Metadata.Name, antigo, novo));
                    }
            }
            else if (entry.Entity is VagaRequisito or VagaAtividade or VagaDiferencial)
            {
                if (entry.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
                {
                    var (lista, vagaId, texto) = DadosLista(entry);
                    Acumular(listas, vagaId, lista, entry, texto);
                }
            }
            else if (entry.Metadata.Name == "VagaBeneficio" &&
                     entry.State is EntityState.Added or EntityState.Deleted)
            {
                var vagaId = (int)entry.Property("VagaId").CurrentValue!;
                var benId = (int)entry.Property("BeneficioId").CurrentValue!;
                if (!beneficios.TryGetValue(vagaId, out var par))
                    beneficios[vagaId] = par = (new HashSet<int>(), new HashSet<int>());
                (entry.State == EntityState.Added ? par.Depois : par.Antes).Add(benId);
            }
        }

        foreach (var ((vagaId, lista), (antes, depois)) in listas)
        {
            // conjunto: itens que existem dos dois lados não contam como mudança
            var removidos = antes.Except(depois).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
            var incluidos = depois.Except(antes).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (removidos.Count == 0 && incluidos.Count == 0) continue;
            logs.Add(Novo("Vaga", vagaId, lista,
                removidos.Count == 0 ? null : string.Join("; ", removidos),
                incluidos.Count == 0 ? null : string.Join("; ", incluidos)));
        }

        foreach (var (vagaId, (antes, depois)) in beneficios)
        {
            var removidos = antes.Except(depois).ToList();
            var incluidos = depois.Except(antes).ToList();
            if (removidos.Count == 0 && incluidos.Count == 0) continue;
            var ids = removidos.Concat(incluidos).ToList();
            var consulta = ctx.Set<Beneficio>().AsNoTracking().Where(b => ids.Contains(b.Id));
            var nomes = ct is null
                ? consulta.ToDictionary(b => b.Id, b => b.Nome)
                : await consulta.ToDictionaryAsync(b => b.Id, b => b.Nome, ct.Value);
            string Nomes(IEnumerable<int> xs) => string.Join("; ",
                xs.Select(i => nomes.GetValueOrDefault(i, $"#{i}")).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
            logs.Add(Novo("Vaga", vagaId, "Benefícios",
                removidos.Count == 0 ? null : Nomes(removidos),
                incluidos.Count == 0 ? null : Nomes(incluidos)));
        }

        if (logs.Count > 0) ctx.Set<LogAuditoria>().AddRange(logs);
    }

    private async Task GravarCriacoes(DbContext ctx, CancellationToken? ct)
    {
        if (_gravandoLogs || _criadasPendentes.Count == 0) return;
        var pendentes = _criadasPendentes.ToList();
        _criadasPendentes.Clear();
        _gravandoLogs = true;
        try
        {
            foreach (var (entidade, tabela) in pendentes)
            {
                var entry = ctx.Entry(entidade);
                ctx.Set<LogAuditoria>().Add(Novo(tabela, ChaveId(entry), "(criado)", null, null));
            }
            if (ct is null) ctx.SaveChanges(); else await ctx.SaveChangesAsync(ct.Value);
        }
        finally { _gravandoLogs = false; }
    }

    private static void Acumular(
        Dictionary<(int VagaId, string Lista), (List<string> Antes, List<string> Depois)> listas,
        int vagaId, string lista, EntityEntry entry, string textoAtual)
    {
        if (!listas.TryGetValue((vagaId, lista), out var par))
            listas[(vagaId, lista)] = par = (new List<string>(), new List<string>());
        switch (entry.State)
        {
            case EntityState.Added: par.Depois.Add(textoAtual); break;
            case EntityState.Deleted: par.Antes.Add(Texto(entry.Property("Texto").OriginalValue) ?? ""); break;
            case EntityState.Modified:
                par.Antes.Add(Texto(entry.Property("Texto").OriginalValue) ?? "");
                par.Depois.Add(textoAtual);
                break;
        }
    }

    private static (string Lista, int VagaId, string Texto) DadosLista(EntityEntry entry) => entry.Entity switch
    {
        VagaRequisito r => ("Requisitos", r.VagaId, r.Texto),
        VagaAtividade a => ("Atividades", a.VagaId, a.Texto),
        VagaDiferencial d => ("Diferenciais", d.VagaId, d.Texto),
        _ => throw new InvalidOperationException(),
    };

    private LogAuditoria Novo(string tabela, int registroId, string campo, string? antigo, string? novo) => new()
    {
        UsuarioRhId = _usuario.UsuarioRhId,
        Tabela = tabela,
        RegistroId = registroId,
        Campo = campo,
        ValorAntigo = antigo,
        ValorNovo = novo,
    };

    private static int ChaveId(EntityEntry entry) => (int)entry.Property("Id").CurrentValue!;

    private static string? Texto(object? v) => v switch
    {
        null => null,
        bool b => b ? "Sim" : "Não",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };
}
