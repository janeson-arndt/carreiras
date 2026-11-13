/* Dá ao pool do IIS "carreiras" acesso ao banco RH_METISA por autenticação integrada (sem senha em arquivo).
   Rode como sysadmin (o instalar-iis.ps1 já faz isso). Pode ser executado de novo. */
USE master;
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'IIS APPPOOL\carreiras')
    CREATE LOGIN [IIS APPPOOL\carreiras] FROM WINDOWS WITH DEFAULT_DATABASE = [RH_METISA];
GO
USE RH_METISA;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IIS APPPOOL\carreiras')
    CREATE USER [IIS APPPOOL\carreiras] FOR LOGIN [IIS APPPOOL\carreiras];
GO
-- leitura e escrita nas tabelas, mais executar as rotinas (anonimização e limpeza)
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\carreiras];
ALTER ROLE db_datawriter ADD MEMBER [IIS APPPOOL\carreiras];
GRANT EXECUTE TO [IIS APPPOOL\carreiras];
-- o Identity cria/ajusta suas tabelas pela migração: sem permissão de DDL aqui (rode as migrações com outra conta)
GO
