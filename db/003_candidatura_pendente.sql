/* ============================================================
   Portal de Carreiras - 003: candidatura pendente de confirmação (Entrega 04)
   SQL Server 2016 SP1 ou superior. Execute DEPOIS do 001 e do 002, no banco RH_METISA.
   Pode ser executado de novo sem efeito (usa verificações).

   Por quê: a Candidatura só nasce quando o candidato clica no link do e-mail (RN07), mas até lá é preciso
   lembrar a vaga, o currículo e a pretensão salarial, e os dados do perfil que ele informou. Em vez de criar
   uma tabela nova, esses campos ficam na própria ConfirmacaoEmail, ligados ao token (que só existe como hash).
   Os dados pessoais pendentes são apagados na confirmação e, se ela nunca vier, pela rotina de limpeza abaixo.
   ============================================================ */
USE RH_METISA;
GO

IF COL_LENGTH('dbo.ConfirmacaoEmail', 'VagaId') IS NULL
BEGIN
    ALTER TABLE dbo.ConfirmacaoEmail ADD
        CriadaEm          datetime2(0)  NOT NULL CONSTRAINT DF_ConfirmacaoEmail_CriadaEm DEFAULT SYSUTCDATETIME(),
        VagaId            int           NULL CONSTRAINT FK_ConfirmacaoEmail_Vaga REFERENCES dbo.Vaga(Id),
        CurriculoId       int           NULL CONSTRAINT FK_ConfirmacaoEmail_Curriculo REFERENCES dbo.Curriculo(Id),
        PretensaoSalarial decimal(10,2) NULL,
        Nome              nvarchar(120) NULL,
        Telefone          nvarchar(20)  NULL,
        Cidade            nvarchar(80)  NULL,
        Uf                char(2)       NULL;
END
GO

/* Candidatos que nunca confirmaram o e-mail e não têm candidatura: o prazo de retenção vence agora, para a rotina
   usp_AnonimizarCandidatosExpirados (que também marca o currículo para o disco ser limpo) cuidar do resto.
   Os dados pendentes de confirmações já usadas ou vencidas há mais que @dias são apagados. */
CREATE OR ALTER PROCEDURE dbo.usp_ExpirarCandidaturasNaoConfirmadas
    @dias int = 7
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;

    DECLARE @limite datetime2(0) = DATEADD(DAY, -@dias, SYSUTCDATETIME());

    UPDATE ce SET Nome = NULL, Telefone = NULL, Cidade = NULL, Uf = NULL, PretensaoSalarial = NULL
    FROM dbo.ConfirmacaoEmail ce
    WHERE (ce.UsadoEm IS NOT NULL OR ce.ExpiraEm < @limite)
      AND (ce.Nome IS NOT NULL OR ce.Telefone IS NOT NULL OR ce.Cidade IS NOT NULL OR ce.Uf IS NOT NULL OR ce.PretensaoSalarial IS NOT NULL);

    UPDATE c SET RetencaoAte = DATEADD(SECOND, -2, SYSUTCDATETIME())  -- 2 s no passado: RetencaoAte tem precisão de segundo
    FROM dbo.Candidato c
    WHERE c.EmailConfirmado = 0 AND c.AnonimizadoEm IS NULL AND c.CriadoEm < @limite
      AND NOT EXISTS (SELECT 1 FROM dbo.Candidatura x WHERE x.CandidatoId = c.Id)
      AND NOT EXISTS (SELECT 1 FROM dbo.ConfirmacaoEmail ce WHERE ce.CandidatoId = c.Id AND ce.UsadoEm IS NULL AND ce.ExpiraEm >= SYSUTCDATETIME());

    SELECT @@ROWCOUNT AS CandidatosExpirados;
    COMMIT TRANSACTION;
END;
GO
