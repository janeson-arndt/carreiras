/* ============================================================
   Portal de Carreiras - 004: direitos do titular (Entrega 05)
   SQL Server 2016 SP1 ou superior. Execute DEPOIS do 003, no banco RH_METISA. Pode ser executado de novo.

   usp_AnonimizarCandidato: o mesmo que usp_AnonimizarCandidatosExpirados faz com os candidatos de prazo vencido,
   mas para UM candidato, a pedido dele (exclusao confirmada por e-mail). As candidaturas ficam (sem identificar
   ninguem); o curriculo e marcado como removido para a aplicacao apagar o arquivo do disco.
   Depois disso o e-mail original fica livre para um novo cadastro.
   ============================================================ */
USE RH_METISA;
GO

CREATE OR ALTER PROCEDURE dbo.usp_AnonimizarCandidato
    @candidatoId int
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM dbo.Candidato WHERE Id = @candidatoId AND AnonimizadoEm IS NULL)
    BEGIN
        COMMIT TRANSACTION;
        SELECT CAST(0 AS int) AS Anonimizados;
        RETURN;
    END

    UPDATE dbo.Curriculo SET RemovidoEm = SYSUTCDATETIME() WHERE CandidatoId = @candidatoId AND RemovidoEm IS NULL;
    UPDATE dbo.ConsentimentoLgpd SET Ip = NULL WHERE CandidatoId = @candidatoId;
    DELETE FROM dbo.ConfirmacaoEmail WHERE CandidatoId = @candidatoId;

    UPDATE dbo.Candidato SET
        Email = CONCAT('anonimizado-', Id, '@invalid.local'),
        Nome = N'Anonimizado',
        Telefone = NULL, Cidade = NULL, Uf = NULL,
        AnonimizadoEm = SYSUTCDATETIME()
    WHERE Id = @candidatoId;

    COMMIT TRANSACTION;
    SELECT CAST(1 AS int) AS Anonimizados;
END;
GO
