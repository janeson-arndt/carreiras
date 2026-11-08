using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carreiras.Infra.Email;

public interface IEmailSender
{
    Task EnviarAsync(string para, string assunto, string corpoTexto, CancellationToken ct = default);
}

public sealed class EmailOpcoes
{
    public string? Host { get; set; }
    public int Porta { get; set; } = 587;
    public bool UsarSsl { get; set; } = true;
    public string? Usuario { get; set; }
    public string? Senha { get; set; }
    public string De { get; set; } = "carreiras@exemplo.local";
    /// <summary>Sem Host configurado, os e-mails são gravados nesta pasta (desenvolvimento).</summary>
    public string PastaSaida { get; set; } = "storage/emails";
}

/// <summary>SMTP configurável. Sem Host, grava o e-mail em arquivo (somente desenvolvimento).</summary>
public sealed class EmailSender : IEmailSender
{
    private readonly EmailOpcoes _op;
    public EmailSender(IOptions<EmailOpcoes> op) => _op = op.Value;

    public async Task EnviarAsync(string para, string assunto, string corpoTexto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_op.Host))
        {
            Directory.CreateDirectory(_op.PastaSaida);
            var nome = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..40] + ".txt";
            await File.WriteAllTextAsync(Path.Combine(_op.PastaSaida, nome),
                $"Para: {para}\nAssunto: {assunto}\n\n{corpoTexto}\n", ct);
            return;
        }

        using var msg = new MailMessage(_op.De, para, assunto, corpoTexto);
        using var smtp = new SmtpClient(_op.Host, _op.Porta) { EnableSsl = _op.UsarSsl };
        if (!string.IsNullOrEmpty(_op.Usuario)) smtp.Credentials = new NetworkCredential(_op.Usuario, _op.Senha);
        await smtp.SendMailAsync(msg, ct);
    }
}
