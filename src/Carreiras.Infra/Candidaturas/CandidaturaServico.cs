using Carreiras.Dominio.Candidaturas;
using Carreiras.Infra.Dados;
using Carreiras.Infra.Dados.Entidades;
using Carreiras.Infra.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Carreiras.Infra.Candidaturas;

public enum StatusEnvio { Ok, Invalido, VagaIndisponivel, JaCandidatou, MuitasTentativas }
public sealed record ResultadoEnvio(StatusEnvio Status, int? ConfirmacaoId = null, Dictionary<string, string>? Erros = null);

public enum StatusConfirmacao { Ok, Invalido, Expirado, Usado, Substituido, VagaIndisponivel, JaCandidatou }
public sealed record ResultadoConfirmacao(
    StatusConfirmacao Status, string? VagaTitulo = null, string? VagaSlug = null, string? EmailMascarado = null);

public sealed class CandidaturaOpcoes
{
    public int HorasValidadeLink { get; set; } = 24;
    public int SegundosEntreEnvios { get; set; } = 60;
    public int MaxEnviosPorHora { get; set; } = 5;
    public int MesesRetencao { get; set; } = 12;
}

/// <summary>
/// Candidatura avulsa com confirmação por e-mail (Entrega 04, RN07/RN08/RN09/RN10).
/// A Candidatura só nasce no clique do link. Até lá os dados ficam na ConfirmacaoEmail, ligados ao token, que
/// existe no banco somente como hash.
/// </summary>
public sealed class CandidaturaServico
{
    public const string FinalidadeCandidatura = "CANDIDATURA";
    private readonly AppDbContext _db;
    private readonly ArmazenamentoCurriculos _arquivos;
    private readonly IEmailSender _email;
    private readonly CandidaturaOpcoes _op;
    private readonly ILogger<CandidaturaServico> _log;

    public CandidaturaServico(AppDbContext db, ArmazenamentoCurriculos arquivos, IEmailSender email,
        Microsoft.Extensions.Options.IOptions<CandidaturaOpcoes> op, ILogger<CandidaturaServico> log)
    {
        _db = db; _arquivos = arquivos; _email = email; _op = op.Value; _log = log;
    }

    /// <summary>Envia a candidatura: valida, guarda o currículo, grava o consentimento e manda o link por e-mail.</summary>
    public async Task<ResultadoEnvio> EnviarAsync(
        string? slugVaga, DadosCandidatura dados, string? nomeArquivo, Stream? arquivo, string? ip,
        Func<string, string> montarLink, CancellationToken ct = default)
    {
        var erros = ValidacaoCandidatura.Validar(dados);
        var res = ArquivoCurriculo.Validar(nomeArquivo, arquivo);
        if (!res.Ok) erros["Curriculo"] = res.Erro!;
        if (erros.Count > 0) return new ResultadoEnvio(StatusEnvio.Invalido, Erros: erros);

        // slugVaga nulo = candidatura espontanea (banco de talentos): VagaId nulo
        int? vagaId = null;
        var tituloVaga = "Banco de talentos";
        if (slugVaga != null)
        {
            var vaga = await _db.Vagas.AsNoTracking()
                .Where(v => v.Slug == slugVaga && v.Status == "PUBLICADA")
                .Select(v => new { v.Id, v.Titulo }).FirstOrDefaultAsync(ct);
            if (vaga == null) return new ResultadoEnvio(StatusEnvio.VagaIndisponivel);
            vagaId = vaga.Id; tituloVaga = vaga.Titulo;
        }

        var email = ValidacaoCandidatura.NormalizarEmail(dados.Email);
        var agora = DateTime.UtcNow;

        var candidato = await _db.Candidatos.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (candidato != null)
        {
            // RN09: uma candidatura por pessoa e vaga
            if (await _db.Candidaturas.AnyAsync(c => c.CandidatoId == candidato.Id && c.VagaId == vagaId, ct))
                return new ResultadoEnvio(StatusEnvio.JaCandidatou);
            var desde = agora.AddHours(-1);
            if (await _db.ConfirmacaoEmails.CountAsync(x => x.CandidatoId == candidato.Id && x.CriadaEm > desde, ct) >= _op.MaxEnviosPorHora)
                return new ResultadoEnvio(StatusEnvio.MuitasTentativas);
        }
        else
        {
            candidato = new Candidato
            {
                Email = email,
                Nome = dados.Nome.Trim(),
                Telefone = dados.Telefone.Trim(),
                Cidade = dados.Cidade.Trim(),
                Uf = dados.Uf.Trim().ToUpperInvariant(),
                EmailConfirmado = false,
                CriadoEm = agora,
                RetencaoAte = agora.AddMonths(_op.MesesRetencao),
            };
            _db.Candidatos.Add(candidato);
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // outro envio do mesmo e-mail criou o candidato no mesmo instante
                _db.Entry(candidato).State = EntityState.Detached;
                candidato = await _db.Candidatos.FirstAsync(c => c.Email == email, ct);
            }
        }

        // currículo: arquivo renomeado, fora do wwwroot
        var caminho = await _arquivos.SalvarAsync(arquivo!, res.Extensao!, ct);
        var curriculo = new Curriculo
        {
            CandidatoId = candidato.Id,
            NomeArquivo = ArquivoCurriculo.NomeParaExibicao(nomeArquivo),
            Caminho = caminho,
            TamanhoKb = res.TamanhoKb,
            EnviadoEm = agora,
        };
        _db.Curriculos.Add(curriculo);

        // RN08: consentimento com versão do termo, data e IP
        _db.ConsentimentoLgpds.Add(new ConsentimentoLgpd
        {
            CandidatoId = candidato.Id, VersaoTermo = TermoPrivacidade.VersaoAtual, AceitoEm = agora, Ip = ip,
        });

        await InvalidarAnterioresAsync(candidato.Id, vagaId, agora, ct);
        await _db.SaveChangesAsync(ct); // gera o Id do currículo

        var (token, hash) = TokenConfirmacao.Gerar();
        var conf = new ConfirmacaoEmail
        {
            CandidatoId = candidato.Id, CurriculoId = curriculo.Id, TokenHash = hash, Finalidade = FinalidadeCandidatura,
            CriadaEm = agora, ExpiraEm = agora.AddHours(_op.HorasValidadeLink), VagaId = vagaId,
            PretensaoSalarial = dados.PretensaoSalarial,
            Nome = dados.Nome.Trim(), Telefone = dados.Telefone.Trim(), Cidade = dados.Cidade.Trim(),
            Uf = dados.Uf.Trim().ToUpperInvariant(),
        };
        _db.ConfirmacaoEmails.Add(conf);
        await _db.SaveChangesAsync(ct);

        await EnviarLinkAsync(email, tituloVaga, montarLink(token), ct);
        return new ResultadoEnvio(StatusEnvio.Ok, conf.Id);
    }

    /// <summary>Gera um link novo para uma candidatura ainda não confirmada (o anterior deixa de valer).</summary>
    public async Task<ResultadoEnvio> ReenviarAsync(int confirmacaoId, Func<string, string> montarLink, CancellationToken ct = default)
    {
        var ant = await _db.ConfirmacaoEmails.Include(x => x.Candidato)
            .FirstOrDefaultAsync(x => x.Id == confirmacaoId && x.Finalidade == FinalidadeCandidatura, ct);
        return await ReenviarAsync(ant, montarLink, ct);
    }

    public async Task<ResultadoEnvio> ReenviarPorTokenAsync(string token, Func<string, string> montarLink, CancellationToken ct = default)
    {
        var hash = TokenConfirmacao.Hash(token ?? "");
        var ant = await _db.ConfirmacaoEmails.Include(x => x.Candidato)
            .FirstOrDefaultAsync(x => x.TokenHash == hash && x.Finalidade == FinalidadeCandidatura, ct);
        return await ReenviarAsync(ant, montarLink, ct);
    }

    private async Task<ResultadoEnvio> ReenviarAsync(ConfirmacaoEmail? ant, Func<string, string> montarLink, CancellationToken ct)
    {
        // sem dados pendentes (já usado ou limpo) não há o que reenviar
        if (ant == null || ant.UsadoEm != null || ant.CurriculoId == null || ant.Nome == null)
            return new ResultadoEnvio(StatusEnvio.Invalido);

        var agora = DateTime.UtcNow;
        var candId = ant.CandidatoId;
        var recente = await _db.ConfirmacaoEmails.AnyAsync(
            x => x.CandidatoId == candId && x.CriadaEm > agora.AddSeconds(-_op.SegundosEntreEnvios), ct);
        var naHora = await _db.ConfirmacaoEmails.CountAsync(x => x.CandidatoId == candId && x.CriadaEm > agora.AddHours(-1), ct);
        if (recente || naHora >= _op.MaxEnviosPorHora) return new ResultadoEnvio(StatusEnvio.MuitasTentativas);

        var tituloVaga = "Banco de talentos";
        if (ant.VagaId != null)
        {
            var vaga = await _db.Vagas.AsNoTracking().Where(v => v.Id == ant.VagaId && v.Status == "PUBLICADA")
                .Select(v => new { v.Titulo }).FirstOrDefaultAsync(ct);
            if (vaga == null) return new ResultadoEnvio(StatusEnvio.VagaIndisponivel);
            tituloVaga = vaga.Titulo;
        }

        var (token, hash) = TokenConfirmacao.Gerar();
        var nova = new ConfirmacaoEmail
        {
            CandidatoId = candId, TokenHash = hash, Finalidade = FinalidadeCandidatura, CriadaEm = agora,
            ExpiraEm = agora.AddHours(_op.HorasValidadeLink), VagaId = ant.VagaId, CurriculoId = ant.CurriculoId,
            PretensaoSalarial = ant.PretensaoSalarial, Nome = ant.Nome, Telefone = ant.Telefone,
            Cidade = ant.Cidade, Uf = ant.Uf,
        };
        // o link antigo deixa de valer e passa os dados adiante
        ant.ExpiraEm = agora; ant.UsadoEm = agora;
        ant.Nome = ant.Telefone = ant.Cidade = ant.Uf = null; ant.PretensaoSalarial = null; ant.CurriculoId = null;
        _db.ConfirmacaoEmails.Add(nova);
        await _db.SaveChangesAsync(ct);

        await EnviarLinkAsync(ant.Candidato.Email, tituloVaga, montarLink(token), ct);
        return new ResultadoEnvio(StatusEnvio.Ok, nova.Id);
    }

    /// <summary>Consulta o link sem consumi-lo (a tela de confirmação por GET não efetiva nada).</summary>
    public async Task<ResultadoConfirmacao> ConsultarAsync(string token, CancellationToken ct = default)
    {
        var hash = TokenConfirmacao.Hash(token ?? "");
        var c = await _db.ConfirmacaoEmails.AsNoTracking().Include(x => x.Candidato)
            .FirstOrDefaultAsync(x => x.TokenHash == hash && x.Finalidade == FinalidadeCandidatura, ct);
        if (c == null) return new ResultadoConfirmacao(StatusConfirmacao.Invalido);

        var vaga = c.VagaId == null ? null : await _db.Vagas.AsNoTracking().Where(v => v.Id == c.VagaId)
            .Select(v => new { v.Titulo, v.Slug, v.Status }).FirstOrDefaultAsync(ct);
        var mask = Mascarar(c.Candidato.Email);
        if (c.UsadoEm != null)
        {
            // usado de verdade (virou candidatura) ou trocado por um link mais novo
            var virou = await _db.Candidaturas.AnyAsync(x => x.CandidatoId == c.CandidatoId && x.VagaId == c.VagaId, ct);
            return new ResultadoConfirmacao(virou ? StatusConfirmacao.Usado : StatusConfirmacao.Substituido, vaga?.Titulo, vaga?.Slug, mask);
        }
        if (c.ExpiraEm <= DateTime.UtcNow) return new ResultadoConfirmacao(StatusConfirmacao.Expirado, vaga?.Titulo, vaga?.Slug, mask);
        if (c.VagaId == null) return new ResultadoConfirmacao(StatusConfirmacao.Ok, null, null, mask);   // banco de talentos
        if (vaga == null || vaga.Status != "PUBLICADA") return new ResultadoConfirmacao(StatusConfirmacao.VagaIndisponivel, vaga?.Titulo, vaga?.Slug, mask);
        return new ResultadoConfirmacao(StatusConfirmacao.Ok, vaga.Titulo, vaga.Slug, mask);
    }

    /// <summary>Efetiva a candidatura. O token é de uso único: o UPDATE condicional garante isso mesmo com dois cliques simultâneos.</summary>
    public async Task<ResultadoConfirmacao> ConfirmarAsync(string token, CancellationToken ct = default)
    {
        var estado = await ConsultarAsync(token, ct);
        if (estado.Status != StatusConfirmacao.Ok) return estado;

        var hash = TokenConfirmacao.Hash(token);
        var agora = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var consumidos = await _db.ConfirmacaoEmails
            .Where(x => x.TokenHash == hash && x.UsadoEm == null && x.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsadoEm, agora), ct);
        if (consumidos != 1)
        {
            await tx.RollbackAsync(ct);
            return (await ConsultarAsync(token, ct)) with { Status = StatusConfirmacao.Usado };
        }

        var conf = await _db.ConfirmacaoEmails.Include(x => x.Candidato).FirstAsync(x => x.TokenHash == hash, ct);
        var cand = conf.Candidato;

        if (await _db.Candidaturas.AnyAsync(x => x.CandidatoId == cand.Id && x.VagaId == conf.VagaId, ct))
        {
            await tx.RollbackAsync(ct);
            return estado with { Status = StatusConfirmacao.JaCandidatou };
        }

        // perfil informado no formulário passa a valer só agora, com o e-mail confirmado
        cand.EmailConfirmado = true;
        if (conf.Nome != null) cand.Nome = conf.Nome;
        if (conf.Telefone != null) cand.Telefone = conf.Telefone;
        if (conf.Cidade != null) cand.Cidade = conf.Cidade;
        if (conf.Uf != null) cand.Uf = conf.Uf;
        cand.RetencaoAte = agora.AddMonths(_op.MesesRetencao);

        var candidatura = new Candidatura
        {
            CandidatoId = cand.Id, VagaId = conf.VagaId, CurriculoId = conf.CurriculoId!.Value,
            PretensaoSalarial = conf.PretensaoSalarial, Status = "RECEBIDA", CriadaEm = agora,
        };
        candidatura.CandidaturaStatusHists.Add(new CandidaturaStatusHist { Status = "RECEBIDA", AlteradoEm = agora });
        _db.Candidaturas.Add(candidatura);

        // dados pessoais pendentes não precisam mais ficar na confirmação
        conf.Nome = conf.Telefone = conf.Cidade = conf.Uf = null;
        conf.PretensaoSalarial = null;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _log.LogInformation("Candidatura {CandidaturaId} confirmada para a vaga {VagaId}", candidatura.Id, candidatura.VagaId);
        return estado;
    }

    /// <summary>Links anteriores ainda abertos deste candidato para a mesma vaga deixam de valer.</summary>
    private async Task InvalidarAnterioresAsync(int candidatoId, int? vagaId, DateTime agora, CancellationToken ct)
    {
        var abertos = await _db.ConfirmacaoEmails
            .Where(x => x.CandidatoId == candidatoId && x.VagaId == vagaId && x.UsadoEm == null && x.ExpiraEm > agora)
            .ToListAsync(ct);
        foreach (var a in abertos)
        {
            a.ExpiraEm = agora; a.UsadoEm = agora;
            a.Nome = a.Telefone = a.Cidade = a.Uf = null; a.PretensaoSalarial = null;
            if (a.CurriculoId != null)
            {
                var cv = await _db.Curriculos.FirstOrDefaultAsync(c => c.Id == a.CurriculoId && c.RemovidoEm == null
                    && !_db.Candidaturas.Any(k => k.CurriculoId == c.Id), ct);
                if (cv != null) cv.RemovidoEm = agora;
                a.CurriculoId = null;
            }
        }
    }

    private async Task EnviarLinkAsync(string para, string tituloVaga, string link, CancellationToken ct)
    {
        var descricao = tituloVaga == "Banco de talentos" ? "seu cadastro no banco de talentos" : "sua candidatura para a vaga \"" + tituloVaga + "\"";
        var corpo =
$@"Olá!

Recebemos {descricao} no Portal de Carreiras.

Para concluir, confirme seu e-mail neste link (vale por {_op.HorasValidadeLink} horas e só pode ser usado uma vez):

{link}

Se não foi você, ignore esta mensagem: nada será enviado ao RH sem a confirmação.

Equipe de Gente e Gestão";
        try { await _email.EnviarAsync(para, "Confirme sua candidatura", corpo, ct); }
        catch (Exception e)
        {
            // sem e-mail, nome ou token no log
            _log.LogError(e, "Falha ao enviar o e-mail de confirmação de candidatura");
            throw;
        }
    }

    public static string Mascarar(string email)
    {
        var i = email.IndexOf('@');
        if (i <= 1) return "***" + (i < 0 ? "" : email[i..]);
        return email[0] + new string('*', Math.Min(i - 1, 5)) + email[i..];
    }
}
