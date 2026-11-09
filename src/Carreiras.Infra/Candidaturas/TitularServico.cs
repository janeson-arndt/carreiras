using System.Globalization;
using System.Text;
using Carreiras.Dominio.Candidaturas;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carreiras.Infra.Candidaturas;

public sealed record ResultadoTitular(StatusConfirmacao Status, string? Tipo = null, string? EmailMascarado = null);

/// <summary>
/// Direitos do titular (Entrega 05): cópia dos dados e exclusão (anonimização), sempre iniciados pelo e-mail do
/// candidato e executados só depois do clique no link de confirmação. Cada pedido vira uma SolicitacaoTitular com as
/// datas de abertura e de atendimento. O link reaproveita ConfirmacaoEmail (Finalidade COPIA ou EXCLUSAO, só o hash).
/// </summary>
public sealed class TitularServico
{
    public const string Exclusao = "EXCLUSAO";
    public const string Copia = "COPIA";
    private static readonly CultureInfo Br = new("pt-BR");

    private readonly AppDbContext _db;
    private readonly ArmazenamentoCurriculos _arquivos;
    private readonly IEmailSender _email;
    private readonly CandidaturaOpcoes _op;
    private readonly ILogger<TitularServico> _log;

    public TitularServico(AppDbContext db, ArmazenamentoCurriculos arquivos, IEmailSender email,
        IOptions<CandidaturaOpcoes> op, ILogger<TitularServico> log)
    {
        _db = db; _arquivos = arquivos; _email = email; _op = op.Value; _log = log;
    }

    /// <summary>
    /// Abre o pedido e manda o link. Quem pede NÃO fica sabendo se o e-mail existe na base: a resposta da tela é a
    /// mesma nos dois casos (evita descobrir quem é candidato). Pedidos em excesso para o mesmo e-mail são ignorados.
    /// </summary>
    public async Task AbrirAsync(string email, string tipo, Func<string, string> montarLink, CancellationToken ct = default)
    {
        if (tipo is not (Exclusao or Copia)) throw new ArgumentException("Tipo inválido.", nameof(tipo));
        var mail = ValidacaoCandidatura.NormalizarEmail(email);
        var cand = await _db.Candidatos.FirstOrDefaultAsync(c => c.Email == mail && c.AnonimizadoEm == null, ct);
        if (cand == null) return;

        var agora = DateTime.UtcNow;
        var naHora = await _db.ConfirmacaoEmails.CountAsync(
            x => x.CandidatoId == cand.Id && (x.Finalidade == Exclusao || x.Finalidade == Copia) && x.CriadaEm > agora.AddHours(-1), ct);
        if (naHora >= 3) return;

        // links anteriores deste mesmo pedido deixam de valer; o pedido aberto é reaproveitado
        var abertos = await _db.ConfirmacaoEmails
            .Where(x => x.CandidatoId == cand.Id && x.Finalidade == tipo && x.UsadoEm == null && x.ExpiraEm > agora).ToListAsync(ct);
        foreach (var a in abertos) { a.ExpiraEm = agora; a.UsadoEm = agora; }

        var sol = await _db.SolicitacaoTitulars.FirstOrDefaultAsync(s => s.CandidatoId == cand.Id && s.Tipo == tipo && s.Status == "ABERTA", ct);
        if (sol == null)
        {
            sol = new SolicitacaoTitular { CandidatoId = cand.Id, Tipo = tipo, Status = "ABERTA", AbertaEm = agora };
            _db.SolicitacaoTitulars.Add(sol);
        }

        var (token, hash) = TokenConfirmacao.Gerar();
        _db.ConfirmacaoEmails.Add(new ConfirmacaoEmail
        {
            CandidatoId = cand.Id, TokenHash = hash, Finalidade = tipo, CriadaEm = agora,
            ExpiraEm = agora.AddHours(_op.HorasValidadeLink),
        });
        await _db.SaveChangesAsync(ct);

        var o = tipo == Exclusao ? "a exclusão dos seus dados" : "uma cópia dos seus dados";
        var aviso = tipo == Exclusao ? "\n\nAtenção: a exclusão não pode ser desfeita." : "";
        var corpo = $@"Olá!

Recebemos um pedido de {o} no Portal de Carreiras da Metisa.

Para confirmar que foi você, clique neste link (vale por {_op.HorasValidadeLink} horas e só pode ser usado uma vez):

{montarLink(token)}{aviso}

Se não foi você, ignore esta mensagem: nada será feito sem a confirmação.

Equipe de Gente e Gestão";
        try { await _email.EnviarAsync(cand.Email, tipo == Exclusao ? "Confirme a exclusão dos seus dados" : "Confirme o pedido de cópia dos seus dados", corpo, ct); }
        catch (Exception e)
        {
            _log.LogError(e, "Falha ao enviar o e-mail de confirmação de pedido do titular");
            throw;
        }
    }

    /// <summary>Consulta o link sem consumi-lo.</summary>
    public async Task<ResultadoTitular> ConsultarAsync(string token, CancellationToken ct = default)
    {
        var hash = TokenConfirmacao.Hash(token ?? "");
        var c = await _db.ConfirmacaoEmails.AsNoTracking().Include(x => x.Candidato)
            .FirstOrDefaultAsync(x => x.TokenHash == hash && (x.Finalidade == Exclusao || x.Finalidade == Copia), ct);
        if (c == null) return new ResultadoTitular(StatusConfirmacao.Invalido);

        var mask = CandidaturaServico.Mascarar(c.Candidato.Email);
        if (c.UsadoEm != null)
        {
            var atendida = await _db.SolicitacaoTitulars.AnyAsync(s => s.CandidatoId == c.CandidatoId && s.Tipo == c.Finalidade && s.Status == "ATENDIDA", ct);
            return new ResultadoTitular(atendida ? StatusConfirmacao.Usado : StatusConfirmacao.Substituido, c.Finalidade, mask);
        }
        if (c.ExpiraEm <= DateTime.UtcNow) return new ResultadoTitular(StatusConfirmacao.Expirado, c.Finalidade, mask);
        return new ResultadoTitular(StatusConfirmacao.Ok, c.Finalidade, mask);
    }

    /// <summary>Executa o pedido. O link é de uso único (UPDATE condicional); se o e-mail da cópia falhar, nada é consumido.</summary>
    public async Task<ResultadoTitular> ConfirmarAsync(string token, CancellationToken ct = default)
    {
        var estado = await ConsultarAsync(token, ct);
        if (estado.Status != StatusConfirmacao.Ok) return estado;

        var hash = TokenConfirmacao.Hash(token);
        var agora = DateTime.UtcNow;
        List<string> arquivosApagar = new();

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            var consumidos = await _db.ConfirmacaoEmails
                .Where(x => x.TokenHash == hash && x.UsadoEm == null && x.ExpiraEm > agora)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsadoEm, agora), ct);
            if (consumidos != 1)
            {
                await tx.RollbackAsync(ct);
                return (await ConsultarAsync(token, ct)) with { Status = StatusConfirmacao.Usado };
            }

            var conf = await _db.ConfirmacaoEmails.AsNoTracking().FirstAsync(x => x.TokenHash == hash, ct);
            var cand = await _db.Candidatos.FirstAsync(c => c.Id == conf.CandidatoId, ct);
            var tipo = conf.Finalidade;

            var sol = await _db.SolicitacaoTitulars.FirstOrDefaultAsync(s => s.CandidatoId == cand.Id && s.Tipo == tipo && s.Status == "ABERTA", ct)
                ?? _db.SolicitacaoTitulars.Add(new SolicitacaoTitular { CandidatoId = cand.Id, Tipo = tipo, AbertaEm = agora, Status = "ABERTA" }).Entity;
            sol.Status = "CONFIRMADA";
            await _db.SaveChangesAsync(ct);

            if (tipo == Copia)
            {
                await _email.EnviarAsync(cand.Email, "Cópia dos seus dados", await MontarCopiaAsync(cand, agora, ct), ct);
            }
            else
            {
                arquivosApagar = await _db.Curriculos.Where(c => c.CandidatoId == cand.Id && c.RemovidoEm == null).Select(c => c.Caminho).ToListAsync(ct);
                await _db.Database.ExecuteSqlRawAsync("EXEC dbo.usp_AnonimizarCandidato @p0", new object[] { cand.Id }, ct);
            }

            sol.Status = "ATENDIDA";
            sol.AtendidaEm = agora;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        foreach (var nome in arquivosApagar) _arquivos.Apagar(nome);
        _log.LogInformation("Pedido do titular ({Tipo}) atendido", estado.Tipo);
        return estado;
    }

    private async Task<string> MontarCopiaAsync(Candidato c, DateTime agora, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Olá!").AppendLine();
        sb.AppendLine($"Esta é a cópia dos dados que o Portal de Carreiras da Metisa guarda sobre você (gerada em {agora.ToLocalTime():dd/MM/yyyy HH:mm}).").AppendLine();
        sb.AppendLine("DADOS DO CADASTRO");
        sb.AppendLine($"Nome: {c.Nome}");
        sb.AppendLine($"E-mail: {c.Email} ({(c.EmailConfirmado ? "confirmado" : "não confirmado")})");
        sb.AppendLine($"Telefone: {c.Telefone ?? "-"}");
        sb.AppendLine($"Cidade/UF: {(c.Cidade == null ? "-" : c.Cidade + "/" + c.Uf)}");
        sb.AppendLine($"Cadastrado em: {c.CriadoEm.ToLocalTime():dd/MM/yyyy}");
        sb.AppendLine($"Guardaremos seus dados até: {c.RetencaoAte.ToLocalTime():dd/MM/yyyy}, salvo novo contato seu.").AppendLine();

        sb.AppendLine("CANDIDATURAS");
        var cands = await _db.Candidaturas.AsNoTracking().Where(x => x.CandidatoId == c.Id)
            .OrderBy(x => x.CriadaEm).Select(x => new { Vaga = x.Vaga == null ? null : x.Vaga.Titulo, x.Status, x.CriadaEm, x.PretensaoSalarial }).ToListAsync(ct);
        if (cands.Count == 0) sb.AppendLine("Nenhuma candidatura confirmada.");
        foreach (var x in cands)
            sb.AppendLine($"- {x.Vaga ?? "Banco de talentos"} | situação: {x.Status} | enviada em {x.CriadaEm.ToLocalTime():dd/MM/yyyy}" +
                          (x.PretensaoSalarial == null ? "" : $" | pretensão salarial: {x.PretensaoSalarial.Value.ToString("C", Br)}"));
        sb.AppendLine();

        sb.AppendLine("CURRÍCULOS");
        var cvs = await _db.Curriculos.AsNoTracking().Where(x => x.CandidatoId == c.Id && x.RemovidoEm == null).OrderBy(x => x.EnviadoEm).ToListAsync(ct);
        if (cvs.Count == 0) sb.AppendLine("Nenhum currículo guardado.");
        foreach (var x in cvs) sb.AppendLine($"- {x.NomeArquivo} ({x.TamanhoKb} KB), enviado em {x.EnviadoEm.ToLocalTime():dd/MM/yyyy}");
        sb.AppendLine();

        sb.AppendLine("ACEITES DO TERMO DE PRIVACIDADE");
        var aceites = await _db.ConsentimentoLgpds.AsNoTracking().Where(x => x.CandidatoId == c.Id).OrderBy(x => x.AceitoEm).ToListAsync(ct);
        foreach (var x in aceites) sb.AppendLine($"- versão {x.VersaoTermo}, em {x.AceitoEm.ToLocalTime():dd/MM/yyyy HH:mm}" + (x.Ip == null ? "" : $", IP {x.Ip}"));
        sb.AppendLine();
        sb.AppendLine("Para pedir a exclusão desses dados, use a página \"Meus dados\" do portal.").AppendLine();
        sb.Append("Equipe de Gente e Gestão");
        return sb.ToString();
    }
}
