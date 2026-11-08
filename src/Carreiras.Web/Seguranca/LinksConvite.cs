using System.Text;
using Carreiras.Infra.Usuarios;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Carreiras.Web.Seguranca;

public static class LinksConvite
{
    /// <summary>Link absoluto da página "Definir senha". O token vai codificado em base64url.</summary>
    public static MontarLink Criar(IUrlHelper url, HttpRequest req) => (uid, token) =>
        url.Page("/DefinirSenha", pageHandler: null,
            values: new { area = "Admin", uid, token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)) },
            protocol: req.Scheme, host: req.Host.ToString())!;
}
