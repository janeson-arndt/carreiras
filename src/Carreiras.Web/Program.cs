using Carreiras.Dominio.Usuarios;
using Carreiras.Infra;
using Carreiras.Infra.Auditoria;
using Carreiras.Infra.Email;
using Carreiras.Infra.Usuarios;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCarreirasInfra(builder.Configuration);
builder.Services.Configure<EmailOpcoes>(builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<GestaoUsuarios>();
builder.Services.AddScoped<Carreiras.Infra.Vagas.VagaServico>();
builder.Services.AddScoped<Carreiras.Infra.Cadastros.CadastroServico>();
builder.Services.AddScoped<Carreiras.Infra.Cadastros.ConteudoServico>();
builder.Services.AddScoped<Carreiras.Infra.Auditoria.AuditoriaConsulta>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUsuarioAuditoria, UsuarioAuditoriaHttp>();

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 10;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<Carreiras.Infra.Identidade.AuthDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<PerfilClaimsFactory>();

// Convite e nova senha: link com expiração (uso único: o carimbo de segurança muda ao definir a senha).
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(48));
// Valida o carimbo de segurança a cada requisição: desativar encerra as sessões abertas na hora.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/admin/entrar";
    o.AccessDeniedPath = "/admin/acesso-negado";
    o.Cookie.Name = "carreiras.rh";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Politicas.ConsultarAdmin, p => p.RequireRole(Perfis.Todos))
    .AddPolicy(Politicas.GerenciarVagas, p => p.RequireRole(Perfis.Administrador, Perfis.Analista))
    .AddPolicy(Politicas.SomenteAdministrador, p => p.RequireRole(Perfis.Administrador));

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeAreaFolder("Admin", "/", Politicas.ConsultarAdmin);
    o.Conventions.AllowAnonymousToAreaPage("Admin", "/Entrar");
    o.Conventions.AllowAnonymousToAreaPage("Admin", "/DefinirSenha");
    o.Conventions.AllowAnonymousToAreaPage("Admin", "/AcessoNegado");
});

var app = builder.Build();

// Comando de linha: cria (ou recupera) um administrador. Senha lida da configuração (user-secrets), nunca de arquivo.
//   dotnet user-secrets set "Admin:Senha" "<senha>" --project src/Carreiras.Web
//   dotnet run --project src/Carreiras.Web -- criar-admin "Nome" email@exemplo.com
if (args.Length > 0 && args[0] == "criar-admin")
{
    return await CriarAdministrador.ExecutarAsync(app.Services, app.Configuration, args);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Content-Security-Policy"] =
        "default-src 'self'; style-src 'self' https://fonts.googleapis.com 'unsafe-inline'; " +
        "font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; script-src 'self'; " +
        "frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
    await next();
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
return 0;

public partial class Program;
