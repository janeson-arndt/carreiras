<#
  Instala e configura o Portal de Carreiras no IIS deste PC, em http://localhost:8080.
  EXECUTE COMO ADMINISTRADOR (PowerShell "Executar como administrador"). Pode ser executado de novo.

  Antes: publique o site em C:\sites\carreiras (dotnet publish src/Carreiras.Web -c Release -o C:\sites\carreiras)
  e coloque lá o web.config (veja src/Carreiras.Web/web.config.exemplo).
#>
param(
    [string]$Pasta = 'C:\sites\carreiras',
    [string]$Dados = 'C:\dados\carreiras',
    [string]$Nome = 'carreiras',
    [int]$Porta = 8080,
    [string]$Sql = 'DESKTOP-CEGRG24\DEVELOPER'
)
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Execute este script como administrador.'
}
if (-not (Test-Path "$Pasta\Carreiras.Web.dll")) { throw "Site não publicado em $Pasta." }
if (-not (Test-Path "$Pasta\web.config")) { throw "Falta o web.config em $Pasta." }

# 1. IIS e filtro de requisições
Write-Host '1/6 Ativando o IIS...'
Enable-WindowsOptionalFeature -Online -NoRestart -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-CommonHttpFeatures, IIS-StaticContent, IIS-DefaultDocument, IIS-HttpErrors, IIS-ApplicationDevelopment, IIS-RequestFiltering, IIS-HttpLogging, IIS-ManagementConsole | Out-Null

# 2. Módulo ASP.NET Core (vem no "Hosting Bundle" do .NET 10)
Write-Host '2/6 Conferindo o módulo ASP.NET Core...'
$modulo = Get-ChildItem "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll" -ErrorAction SilentlyContinue
if (-not $modulo) {
    Write-Warning 'Módulo ASP.NET Core (V2) não encontrado. Instale o ".NET 10 Hosting Bundle" (https://dotnet.microsoft.com/download/dotnet/10.0), depois rode este script de novo.'
    return
}

# 3. Pastas de dados (currículos e e-mails) fora do site
Write-Host '3/6 Pastas de dados...'
New-Item -ItemType Directory -Force "$Dados\curriculos", "$Dados\emails" | Out-Null

# 4. Pool sem código gerenciado + site
Import-Module WebAdministration
Write-Host '4/6 Pool e site...'
if (-not (Test-Path "IIS:\AppPools\$Nome")) { New-WebAppPool -Name $Nome | Out-Null }
Set-ItemProperty "IIS:\AppPools\$Nome" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$Nome" -Name processModel.loadUserProfile -Value $true
if (-not (Test-Path "IIS:\Sites\$Nome")) {
    New-Website -Name $Nome -PhysicalPath $Pasta -ApplicationPool $Nome -Port $Porta -HostHeader 'localhost' | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$Nome" -Name physicalPath -Value $Pasta
}

# 5. Permissões: leitura no site, escrita nos dados
Write-Host '5/6 Permissões...'
$id = "IIS AppPool\$Nome"
icacls $Pasta /grant "${id}:(OI)(CI)RX" /T | Out-Null
icacls $Dados /grant "${id}:(OI)(CI)M" /T | Out-Null

# 6. Acesso do pool ao SQL Server (autenticação integrada, sem senha em arquivo)
Write-Host '6/6 Acesso ao SQL Server...'
$sqlScript = Join-Path $PSScriptRoot 'permissoes-sql.sql'
sqlcmd -S $Sql -E -C -I -b -i $sqlScript
if ($LASTEXITCODE -ne 0) { Write-Warning "Não consegui aplicar $sqlScript com a sua conta do Windows. Rode-o no SSMS como sysadmin." }

Restart-WebAppPool $Nome
Write-Host "Pronto: http://localhost:$Porta"
