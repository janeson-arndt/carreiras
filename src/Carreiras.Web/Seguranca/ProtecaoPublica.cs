using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.AspNetCore.DataProtection;

namespace Carreiras.Web.Seguranca;

/// <summary>
/// Desafio simples contra envio automático (conta de somar). O resultado vai dentro de um token assinado e com
/// validade, então não precisa de sessão nem de armazenamento no servidor.
/// </summary>
public sealed class DesafioHumano
{
    private readonly ITimeLimitedDataProtector _p;
    public DesafioHumano(IDataProtectionProvider dp) =>
        _p = dp.CreateProtector("carreiras.desafio-humano").ToTimeLimitedDataProtector();

    public (string Pergunta, string Token) Criar()
    {
        var a = Random.Shared.Next(2, 10);
        var b = Random.Shared.Next(2, 10);
        return ($"Quanto é {a} + {b}?", _p.Protect((a + b).ToString(CultureInfo.InvariantCulture), TimeSpan.FromHours(1)));
    }

    public bool Validar(string? token, string? resposta)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(resposta)) return false;
        try { return _p.Unprotect(token) == resposta.Trim(); }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }
}

/// <summary>Referência assinada e com validade para a tela "Falta um passo" (id da confirmação + e-mail mascarado).</summary>
public sealed class ReferenciaEnvio
{
    private readonly ITimeLimitedDataProtector _p;
    public ReferenciaEnvio(IDataProtectionProvider dp) =>
        _p = dp.CreateProtector("carreiras.referencia-envio").ToTimeLimitedDataProtector();

    public string Criar(int confirmacaoId, string emailMascarado) =>
        _p.Protect($"{confirmacaoId}|{emailMascarado}", TimeSpan.FromHours(24));

    public (int Id, string Email)? Ler(string? r)
    {
        if (string.IsNullOrWhiteSpace(r)) return null;
        try
        {
            var partes = _p.Unprotect(r).Split('|', 2);
            return partes.Length == 2 && int.TryParse(partes[0], out var id) ? (id, partes[1]) : null;
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }
}

public sealed class LimiteOpcoes
{
    public int Tentativas { get; set; } = 10;
    public int JanelaMinutos { get; set; } = 10;
}

/// <summary>Limite de tentativas por origem (IP) nos endpoints públicos que gravam dados. Em memória, janela deslizante.</summary>
public sealed class LimitadorTentativas
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _tentativas = new();
    private readonly LimiteOpcoes _op;
    public LimitadorTentativas(Microsoft.Extensions.Options.IOptions<LimiteOpcoes> op) => _op = op.Value;

    /// <summary>Registra a tentativa e diz se ela é permitida. <paramref name="escopo"/> separa os contadores por endpoint.</summary>
    public bool Permitir(string escopo, string? ip)
    {
        var agora = DateTime.UtcNow;
        var inicio = agora.AddMinutes(-_op.JanelaMinutos);
        var fila = _tentativas.GetOrAdd($"{escopo}|{ip ?? "?"}", _ => new Queue<DateTime>());
        lock (fila)
        {
            while (fila.Count > 0 && fila.Peek() < inicio) fila.Dequeue();
            if (fila.Count >= _op.Tentativas) return false;
            fila.Enqueue(agora);
        }
        if (_tentativas.Count > 20_000)
            foreach (var k in _tentativas.Where(x => { lock (x.Value) return x.Value.Count == 0 || x.Value.Peek() < inicio; }).Select(x => x.Key).ToList())
                _tentativas.TryRemove(k, out _);
        return true;
    }
}
