/* ============================================================
   Portal de Carreiras - 002: gestão de usuários do RH (Entrega 02)
   SQL Server 2016 SP1 ou superior. Execute DEPOIS do 001 (carreiras_entrega01.sql),
   no banco RH_METISA. Não é reexecutável.

   Se uma versão anterior deste script (com o gatilho trg_UsuarioRh_UltimoAdmin) já foi
   executada, rode antes:  DROP TRIGGER IF EXISTS dbo.trg_UsuarioRh_UltimoAdmin;

   Regras que este script apoia:
   - Usuário não é excluído: só desativado (Ativo = 0). Isso preserva o histórico
     em LogAuditoria, VagaStatusHist e CandidaturaStatusHist.
   - O sistema nunca fica sem administrador ativo. Essa regra vive SÓ na aplicação,
     dentro de uma transação (veja as observações no fim); o banco não tem gatilho.
   - O administrador não define nem vê a senha de ninguém: a pessoa recebe um
     convite por e-mail e cria a própria senha.
   ============================================================ */
USE RH_METISA;
GO

/* ---------- A) Campos de controle (valem com ou sem ASP.NET Identity) ---------- */
ALTER TABLE dbo.UsuarioRh ADD
    CriadoEm        datetime2(0) NOT NULL CONSTRAINT DF_UsuarioRh_CriadoEm DEFAULT SYSUTCDATETIME(),
    UltimoAcessoEm  datetime2(0) NULL,
    CriadoPorUsuarioRhId int NULL CONSTRAINT FK_UsuarioRh_CriadoPor REFERENCES dbo.UsuarioRh(Id);
GO

/* ---------- B) Vínculo com ASP.NET Identity (RECOMENDADO) ----------
   Identity guarda a credencial (hash da senha, bloqueio por tentativas, carimbo de
   segurança que encerra sessões ao desativar) e gera os links de convite e de nova
   senha. UsuarioRh continua sendo a tabela do negócio (perfil, situação, histórico),
   ligada a AspNetUsers por IdentityUserId. As chaves estrangeiras de auditoria e
   histórico seguem apontando para UsuarioRh.Id.
   SenhaHash deixa de ser usada: passa a aceitar nulo.
   Se a decisão for NÃO usar Identity, pule esta parte e crie uma tabela de convites. */
ALTER TABLE dbo.UsuarioRh ADD IdentityUserId nvarchar(450) NULL;
GO
CREATE UNIQUE INDEX UX_UsuarioRh_IdentityUserId ON dbo.UsuarioRh (IdentityUserId) WHERE IdentityUserId IS NOT NULL;
GO
ALTER TABLE dbo.UsuarioRh ALTER COLUMN SenhaHash nvarchar(400) NULL;
GO

/* ---------- Observações para a aplicação ----------
   1. LogAuditoria NUNCA deve receber senha, hash, token ou link de convite.
      Registre apenas o fato (ex.: campo "Convite enviado") e quem o fez.
   2. Ao desativar, atualize o carimbo de segurança no Identity para encerrar as
      sessões abertas da pessoa.
   3. Último administrador: verifique e altere na MESMA transação, travando as linhas dos
      administradores (UPDLOCK, HOLDLOCK ou isolamento Serializable). Sem isso, duas
      desativações simultâneas podem deixar o portal sem administrador.
   4. O primeiro administrador não vem no seed. Crie-o pela aplicação
      (comando de linha ou configuração inicial), com a senha lida de user-secrets.
   ============================================================ */
