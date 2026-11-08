/* ============================================================
   Portal de Carreiras - Entrega 01: modelagem do banco
   SQL Server 2016 SP1 ou superior. Execute no SSMS ou via sqlcmd.
   Projeto de estudo: conteudo marcado Ficticio = 1 e de exemplo.
   ============================================================ */
/* O banco RH_METISA ja deve existir. Este script nao o cria. */
USE RH_METISA;
GO

/* ---------- Tabelas de dominio ---------- */
CREATE TABLE dbo.Area (
    Id    int IDENTITY(1,1) CONSTRAINT PK_Area PRIMARY KEY,
    Nome  nvarchar(80) NOT NULL CONSTRAINT UQ_Area_Nome UNIQUE,
    Ativo bit NOT NULL CONSTRAINT DF_Area_Ativo DEFAULT 1
);
CREATE TABLE dbo.Turno (
    Id    int IDENTITY(1,1) CONSTRAINT PK_Turno PRIMARY KEY,
    Nome  nvarchar(80) NOT NULL CONSTRAINT UQ_Turno_Nome UNIQUE,
    Ativo bit NOT NULL CONSTRAINT DF_Turno_Ativo DEFAULT 1
);
CREATE TABLE dbo.TipoContrato (
    Id    int IDENTITY(1,1) CONSTRAINT PK_TipoContrato PRIMARY KEY,
    Nome  nvarchar(80) NOT NULL CONSTRAINT UQ_TipoContrato_Nome UNIQUE,
    Ativo bit NOT NULL CONSTRAINT DF_TipoContrato_Ativo DEFAULT 1
);
CREATE TABLE dbo.Modalidade (
    Id    int IDENTITY(1,1) CONSTRAINT PK_Modalidade PRIMARY KEY,
    Nome  nvarchar(80) NOT NULL CONSTRAINT UQ_Modalidade_Nome UNIQUE,
    Ativo bit NOT NULL CONSTRAINT DF_Modalidade_Ativo DEFAULT 1
);
CREATE TABLE dbo.Unidade (
    Id       int IDENTITY(1,1) CONSTRAINT PK_Unidade PRIMARY KEY,
    Nome     nvarchar(100) NOT NULL CONSTRAINT UQ_Unidade_Nome UNIQUE,
    Tipo     varchar(25) NOT NULL
             CONSTRAINT CK_Unidade_Tipo CHECK (Tipo IN ('MATRIZ','CENTRO_DISTRIBUICAO','ESCRITORIO_COMERCIAL')),
    Endereco nvarchar(150) NOT NULL,
    Bairro   nvarchar(80) NULL,
    Cidade   nvarchar(80) NOT NULL,
    Uf       char(2) NOT NULL,
    Cep      varchar(9) NULL,
    Telefone nvarchar(60) NULL,
    Ativo    bit NOT NULL CONSTRAINT DF_Unidade_Ativo DEFAULT 1
);
CREATE TABLE dbo.Programa (
    Id           int IDENTITY(1,1) CONSTRAINT PK_Programa PRIMARY KEY,
    Nome         nvarchar(100) NOT NULL CONSTRAINT UQ_Programa_Nome UNIQUE,
    Descricao    nvarchar(600) NULL,
    PublicoAlvo  nvarchar(200) NULL,
    Ativo        bit NOT NULL CONSTRAINT DF_Programa_Ativo DEFAULT 1,
    Ficticio     bit NOT NULL CONSTRAINT DF_Programa_Ficticio DEFAULT 0
);
GO

/* ---------- Beneficios (categoria, icone e ordem para a vitrine) ---------- */
CREATE TABLE dbo.BeneficioCategoria (
    Id    int IDENTITY(1,1) CONSTRAINT PK_BeneficioCategoria PRIMARY KEY,
    Nome  nvarchar(60) NOT NULL CONSTRAINT UQ_BeneficioCategoria_Nome UNIQUE,
    Ordem int NOT NULL CONSTRAINT DF_BeneficioCategoria_Ordem DEFAULT 0
);
CREATE TABLE dbo.Beneficio (
    Id             int IDENTITY(1,1) CONSTRAINT PK_Beneficio PRIMARY KEY,
    CategoriaId    int NOT NULL CONSTRAINT FK_Beneficio_Categoria REFERENCES dbo.BeneficioCategoria(Id),
    Nome           nvarchar(80) NOT NULL CONSTRAINT UQ_Beneficio_Nome UNIQUE,
    DescricaoCurta nvarchar(160) NULL,
    Icone          varchar(50) NULL,
    Ordem          int NOT NULL CONSTRAINT DF_Beneficio_Ordem DEFAULT 0,
    Ativo          bit NOT NULL CONSTRAINT DF_Beneficio_Ativo DEFAULT 1,
    Ficticio       bit NOT NULL CONSTRAINT DF_Beneficio_Ficticio DEFAULT 0
);
GO

/* ---------- Administracao (RH) ---------- */
CREATE TABLE dbo.PerfilRh (
    Id   int IDENTITY(1,1) CONSTRAINT PK_PerfilRh PRIMARY KEY,
    Nome nvarchar(40) NOT NULL CONSTRAINT UQ_PerfilRh_Nome UNIQUE
);
/* Se usar ASP.NET Identity, esta tabela pode virar apenas o vinculo com o perfil. */
CREATE TABLE dbo.UsuarioRh (
    Id        int IDENTITY(1,1) CONSTRAINT PK_UsuarioRh PRIMARY KEY,
    Nome      nvarchar(120) NOT NULL,
    Email     nvarchar(256) NOT NULL CONSTRAINT UQ_UsuarioRh_Email UNIQUE,
    SenhaHash nvarchar(400) NOT NULL,
    PerfilId  int NOT NULL CONSTRAINT FK_UsuarioRh_Perfil REFERENCES dbo.PerfilRh(Id),
    Ativo     bit NOT NULL CONSTRAINT DF_UsuarioRh_Ativo DEFAULT 1
);
CREATE TABLE dbo.LogAuditoria (
    Id           bigint IDENTITY(1,1) CONSTRAINT PK_LogAuditoria PRIMARY KEY,
    UsuarioRhId  int NULL CONSTRAINT FK_LogAuditoria_Usuario REFERENCES dbo.UsuarioRh(Id),
    Tabela       nvarchar(128) NOT NULL,
    RegistroId   int NOT NULL,
    Campo        nvarchar(128) NOT NULL,
    ValorAntigo  nvarchar(max) NULL,
    ValorNovo    nvarchar(max) NULL,
    DataHora     datetime2(0) NOT NULL CONSTRAINT DF_LogAuditoria_DataHora DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_LogAuditoria_Tabela_Registro ON dbo.LogAuditoria (Tabela, RegistroId, DataHora);
GO

/* ---------- Vaga e conteudo ---------- */
CREATE TABLE dbo.Vaga (
    Id             int IDENTITY(1,1) CONSTRAINT PK_Vaga PRIMARY KEY,
    Slug           nvarchar(150) NOT NULL CONSTRAINT UQ_Vaga_Slug UNIQUE,
    Titulo         nvarchar(150) NOT NULL,
    Descricao      nvarchar(max) NOT NULL,
    UnidadeId      int NOT NULL CONSTRAINT FK_Vaga_Unidade REFERENCES dbo.Unidade(Id),
    AreaId         int NOT NULL CONSTRAINT FK_Vaga_Area REFERENCES dbo.Area(Id),
    TurnoId        int NOT NULL CONSTRAINT FK_Vaga_Turno REFERENCES dbo.Turno(Id),
    TipoContratoId int NOT NULL CONSTRAINT FK_Vaga_TipoContrato REFERENCES dbo.TipoContrato(Id),
    ModalidadeId   int NOT NULL CONSTRAINT FK_Vaga_Modalidade REFERENCES dbo.Modalidade(Id),
    ProgramaId     int NULL CONSTRAINT FK_Vaga_Programa REFERENCES dbo.Programa(Id),
    Status         varchar(15) NOT NULL CONSTRAINT DF_Vaga_Status DEFAULT 'RASCUNHO'
                   CONSTRAINT CK_Vaga_Status CHECK (Status IN ('RASCUNHO','PUBLICADA','PAUSADA','ENCERRADA')),
    SalarioMin     decimal(10,2) NULL,
    SalarioMax     decimal(10,2) NULL,
    ExibirSalario  bit NOT NULL CONSTRAINT DF_Vaga_ExibirSalario DEFAULT 0,
    PublicadaEm    datetime2(0) NULL,
    CriadaEm       datetime2(0) NOT NULL CONSTRAINT DF_Vaga_CriadaEm DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Vaga_Salario CHECK (SalarioMax IS NULL OR SalarioMin IS NULL OR SalarioMax >= SalarioMin)
);
CREATE INDEX IX_Vaga_Listagem ON dbo.Vaga (Status, UnidadeId, AreaId, TurnoId)
    INCLUDE (Slug, Titulo, PublicadaEm);

CREATE TABLE dbo.VagaBeneficio (
    VagaId      int NOT NULL CONSTRAINT FK_VagaBeneficio_Vaga REFERENCES dbo.Vaga(Id) ON DELETE CASCADE,
    BeneficioId int NOT NULL CONSTRAINT FK_VagaBeneficio_Beneficio REFERENCES dbo.Beneficio(Id),
    CONSTRAINT PK_VagaBeneficio PRIMARY KEY (VagaId, BeneficioId)
);
CREATE TABLE dbo.VagaRequisito (
    Id          int IDENTITY(1,1) CONSTRAINT PK_VagaRequisito PRIMARY KEY,
    VagaId      int NOT NULL CONSTRAINT FK_VagaRequisito_Vaga REFERENCES dbo.Vaga(Id) ON DELETE CASCADE,
    Texto       nvarchar(300) NOT NULL,
    Obrigatorio bit NOT NULL CONSTRAINT DF_VagaRequisito_Obrig DEFAULT 1
);
CREATE TABLE dbo.VagaAtividade (
    Id     int IDENTITY(1,1) CONSTRAINT PK_VagaAtividade PRIMARY KEY,
    VagaId int NOT NULL CONSTRAINT FK_VagaAtividade_Vaga REFERENCES dbo.Vaga(Id) ON DELETE CASCADE,
    Texto  nvarchar(300) NOT NULL
);
CREATE TABLE dbo.VagaDiferencial (
    Id     int IDENTITY(1,1) CONSTRAINT PK_VagaDiferencial PRIMARY KEY,
    VagaId int NOT NULL CONSTRAINT FK_VagaDiferencial_Vaga REFERENCES dbo.Vaga(Id) ON DELETE CASCADE,
    Texto  nvarchar(300) NOT NULL
);
CREATE TABLE dbo.VagaStatusHist (
    Id          int IDENTITY(1,1) CONSTRAINT PK_VagaStatusHist PRIMARY KEY,
    VagaId      int NOT NULL CONSTRAINT FK_VagaStatusHist_Vaga REFERENCES dbo.Vaga(Id) ON DELETE CASCADE,
    Status      varchar(15) NOT NULL,
    AlteradoEm  datetime2(0) NOT NULL CONSTRAINT DF_VagaStatusHist_Em DEFAULT SYSUTCDATETIME(),
    UsuarioRhId int NULL CONSTRAINT FK_VagaStatusHist_Usuario REFERENCES dbo.UsuarioRh(Id)
);
GO

/* ---------- Candidato, candidatura e LGPD ---------- */
CREATE TABLE dbo.Candidato (
    Id              int IDENTITY(1,1) CONSTRAINT PK_Candidato PRIMARY KEY,
    Email           nvarchar(256) NOT NULL CONSTRAINT UQ_Candidato_Email UNIQUE,
    Nome            nvarchar(120) NOT NULL,
    Telefone        nvarchar(20) NULL,
    Cidade          nvarchar(80) NULL,
    Uf              char(2) NULL,
    EmailConfirmado bit NOT NULL CONSTRAINT DF_Candidato_EmailConf DEFAULT 0,
    CriadoEm        datetime2(0) NOT NULL CONSTRAINT DF_Candidato_CriadoEm DEFAULT SYSUTCDATETIME(),
    RetencaoAte     datetime2(0) NOT NULL CONSTRAINT DF_Candidato_Retencao DEFAULT DATEADD(MONTH, 12, SYSUTCDATETIME()),
    AnonimizadoEm   datetime2(0) NULL
);
CREATE INDEX IX_Candidato_Retencao ON dbo.Candidato (RetencaoAte) WHERE AnonimizadoEm IS NULL;

CREATE TABLE dbo.Curriculo (
    Id          int IDENTITY(1,1) CONSTRAINT PK_Curriculo PRIMARY KEY,
    CandidatoId int NOT NULL CONSTRAINT FK_Curriculo_Candidato REFERENCES dbo.Candidato(Id),
    NomeArquivo nvarchar(200) NOT NULL,
    Caminho     nvarchar(400) NOT NULL,
    TamanhoKb   int NOT NULL CONSTRAINT CK_Curriculo_Tamanho CHECK (TamanhoKb BETWEEN 1 AND 5120),
    EnviadoEm   datetime2(0) NOT NULL CONSTRAINT DF_Curriculo_EnviadoEm DEFAULT SYSUTCDATETIME(),
    RemovidoEm  datetime2(0) NULL
);
CREATE TABLE dbo.Candidatura (
    Id                int IDENTITY(1,1) CONSTRAINT PK_Candidatura PRIMARY KEY,
    CandidatoId       int NOT NULL CONSTRAINT FK_Candidatura_Candidato REFERENCES dbo.Candidato(Id),
    VagaId            int NULL CONSTRAINT FK_Candidatura_Vaga REFERENCES dbo.Vaga(Id),
    CurriculoId       int NOT NULL CONSTRAINT FK_Candidatura_Curriculo REFERENCES dbo.Curriculo(Id),
    PretensaoSalarial decimal(10,2) NULL,
    Status            varchar(15) NOT NULL CONSTRAINT DF_Candidatura_Status DEFAULT 'RECEBIDA'
                      CONSTRAINT CK_Candidatura_Status
                      CHECK (Status IN ('RECEBIDA','EM_ANALISE','ENTREVISTA','APROVADA','REPROVADA')),
    CriadaEm          datetime2(0) NOT NULL CONSTRAINT DF_Candidatura_CriadaEm DEFAULT SYSUTCDATETIME()
);
/* VagaId nulo = candidatura espontanea (banco de talentos). Impede candidatura repetida na mesma vaga. */
CREATE UNIQUE INDEX UX_Candidatura_Candidato_Vaga ON dbo.Candidatura (CandidatoId, VagaId) WHERE VagaId IS NOT NULL;
CREATE INDEX IX_Candidatura_Vaga ON dbo.Candidatura (VagaId, Status);

CREATE TABLE dbo.CandidaturaStatusHist (
    Id            int IDENTITY(1,1) CONSTRAINT PK_CandidaturaStatusHist PRIMARY KEY,
    CandidaturaId int NOT NULL CONSTRAINT FK_CandStatusHist_Candidatura REFERENCES dbo.Candidatura(Id) ON DELETE CASCADE,
    Status        varchar(15) NOT NULL,
    AlteradoEm    datetime2(0) NOT NULL CONSTRAINT DF_CandStatusHist_Em DEFAULT SYSUTCDATETIME(),
    UsuarioRhId   int NULL CONSTRAINT FK_CandStatusHist_Usuario REFERENCES dbo.UsuarioRh(Id)
);
CREATE TABLE dbo.ConsentimentoLgpd (
    Id          int IDENTITY(1,1) CONSTRAINT PK_ConsentimentoLgpd PRIMARY KEY,
    CandidatoId int NOT NULL CONSTRAINT FK_Consentimento_Candidato REFERENCES dbo.Candidato(Id),
    VersaoTermo varchar(20) NOT NULL,
    AceitoEm    datetime2(0) NOT NULL CONSTRAINT DF_Consentimento_AceitoEm DEFAULT SYSUTCDATETIME(),
    Ip          varchar(45) NULL
);
CREATE TABLE dbo.ConfirmacaoEmail (
    Id          int IDENTITY(1,1) CONSTRAINT PK_ConfirmacaoEmail PRIMARY KEY,
    CandidatoId int NOT NULL CONSTRAINT FK_ConfirmacaoEmail_Candidato REFERENCES dbo.Candidato(Id),
    TokenHash   varchar(128) NOT NULL,
    Finalidade  varchar(15) NOT NULL
                CONSTRAINT CK_ConfirmacaoEmail_Finalidade CHECK (Finalidade IN ('CANDIDATURA','EXCLUSAO','COPIA')),
    ExpiraEm    datetime2(0) NOT NULL,
    UsadoEm     datetime2(0) NULL
);
CREATE INDEX IX_ConfirmacaoEmail_Token ON dbo.ConfirmacaoEmail (TokenHash);
CREATE TABLE dbo.SolicitacaoTitular (
    Id          int IDENTITY(1,1) CONSTRAINT PK_SolicitacaoTitular PRIMARY KEY,
    CandidatoId int NOT NULL CONSTRAINT FK_Solicitacao_Candidato REFERENCES dbo.Candidato(Id),
    Tipo        varchar(10) NOT NULL CONSTRAINT CK_Solicitacao_Tipo CHECK (Tipo IN ('EXCLUSAO','COPIA')),
    Status      varchar(12) NOT NULL CONSTRAINT DF_Solicitacao_Status DEFAULT 'ABERTA'
                CONSTRAINT CK_Solicitacao_Status CHECK (Status IN ('ABERTA','CONFIRMADA','ATENDIDA','NEGADA')),
    AbertaEm    datetime2(0) NOT NULL CONSTRAINT DF_Solicitacao_AbertaEm DEFAULT SYSUTCDATETIME(),
    AtendidaEm  datetime2(0) NULL
);
GO

/* ---------- Conteudo institucional da vitrine ---------- */
CREATE TABLE dbo.ConteudoInstitucional (
    Id        int IDENTITY(1,1) CONSTRAINT PK_ConteudoInstitucional PRIMARY KEY,
    Secao     varchar(40) NOT NULL CONSTRAINT UQ_ConteudoInstitucional_Secao UNIQUE,
    Titulo    nvarchar(120) NOT NULL,
    Texto     nvarchar(max) NOT NULL,
    Ordem     int NOT NULL CONSTRAINT DF_ConteudoInstitucional_Ordem DEFAULT 0,
    Publicado bit NOT NULL CONSTRAINT DF_ConteudoInstitucional_Publicado DEFAULT 1,
    Ficticio  bit NOT NULL CONSTRAINT DF_ConteudoInstitucional_Ficticio DEFAULT 0
);
CREATE TABLE dbo.Documento (
    Id         int IDENTITY(1,1) CONSTRAINT PK_Documento PRIMARY KEY,
    ConteudoId int NOT NULL CONSTRAINT FK_Documento_Conteudo REFERENCES dbo.ConteudoInstitucional(Id) ON DELETE CASCADE,
    Titulo     nvarchar(150) NOT NULL,
    Caminho    nvarchar(400) NOT NULL
);
GO

/* ---------- Retencao LGPD: anonimiza candidatos com prazo vencido ----------
   Mantem o historico de candidaturas, mas remove os dados pessoais.
   O e-mail e trocado por valor nao identificavel (a coluna e unica e obrigatoria).
   Os arquivos de curriculo sao apagados do disco pelo job da aplicacao,
   que le os registros com RemovidoEm preenchido. */
CREATE OR ALTER PROCEDURE dbo.usp_AnonimizarCandidatosExpirados
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRANSACTION;

    DECLARE @alvo TABLE (Id int PRIMARY KEY);
    INSERT @alvo (Id)
    SELECT Id FROM dbo.Candidato
    WHERE AnonimizadoEm IS NULL AND RetencaoAte < SYSUTCDATETIME();

    UPDATE cu SET RemovidoEm = SYSUTCDATETIME()
    FROM dbo.Curriculo cu JOIN @alvo a ON a.Id = cu.CandidatoId
    WHERE cu.RemovidoEm IS NULL;

    UPDATE co SET Ip = NULL
    FROM dbo.ConsentimentoLgpd co JOIN @alvo a ON a.Id = co.CandidatoId;

    DELETE ce FROM dbo.ConfirmacaoEmail ce JOIN @alvo a ON a.Id = ce.CandidatoId;

    UPDATE c SET
        Email = CONCAT('anonimizado-', c.Id, '@invalid.local'),
        Nome = N'Anonimizado',
        Telefone = NULL, Cidade = NULL, Uf = NULL,
        AnonimizadoEm = SYSUTCDATETIME()
    FROM dbo.Candidato c JOIN @alvo a ON a.Id = c.Id;

    COMMIT TRANSACTION;
END;
GO

/* =====================  SEED  ===================== */

INSERT dbo.PerfilRh (Nome) VALUES (N'ADMINISTRADOR'), (N'ANALISTA'), (N'LEITURA');

/* Dados das unidades conforme o site da empresa. O CEP do escritorio de Canoas fica nulo:
   o numero publicado no site tem 7 digitos. O e-mail do encarregado de dados e fictício. */
INSERT dbo.Unidade (Nome, Tipo, Endereco, Bairro, Cidade, Uf, Cep, Telefone) VALUES
 (N'Matriz Timbó', 'MATRIZ', N'Rua Fritz Lorenz, 2442', N'Fritz Lorenz', N'Timbó', 'SC', '89092-600', N'(47) 3281-2222'),
 (N'Filial Recife', 'CENTRO_DISTRIBUICAO', N'Av. Eng.º Alves de Souza, 356', N'Imbiribeira', N'Recife', 'PE', '51170-300', N'(81) 3087-2377'),
 (N'Escritório Rio Grande do Sul', 'ESCRITORIO_COMERCIAL', N'Rua Domingos Martins, 121, Sala 803', N'Centro', N'Canoas', 'RS', NULL, N'(51) 3472-3454 / (51) 3472-7607');

INSERT dbo.Area (Nome) VALUES (N'Produção'), (N'Manutenção'), (N'Qualidade e Laboratório'), (N'Logística'),
    (N'Engenharia'), (N'Comercial'), (N'Segurança do Trabalho'), (N'Administrativo');
INSERT dbo.Turno (Nome) VALUES (N'Comercial (dia)'), (N'1º turno'), (N'2º turno'), (N'3º turno'), (N'Escala 12x36');
INSERT dbo.TipoContrato (Nome) VALUES (N'CLT'), (N'Estágio'), (N'Jovem Aprendiz'), (N'Temporário');
INSERT dbo.Modalidade (Nome) VALUES (N'Presencial'), (N'Híbrido'), (N'Remoto');

INSERT dbo.Programa (Nome, Descricao, PublicoAlvo, Ficticio) VALUES
 (N'Programa de Estágio (exemplo)', N'Estágio com acompanhamento de um tutor e rodízio entre áreas da fábrica.', N'Estudantes de engenharia e cursos técnicos', 1),
 (N'Jovem Aprendiz (exemplo)', N'Primeira experiência profissional com formação técnica na área industrial.', N'Jovens de 14 a 24 anos', 1),
 (N'Trainee Industrial (exemplo)', N'Trilha de desenvolvimento para recém-formados em posições de liderança industrial.', N'Recém-graduados em engenharia', 1);

INSERT dbo.BeneficioCategoria (Nome, Ordem) VALUES
 (N'Financeiro', 1), (N'Educação', 2), (N'Saúde e bem-estar', 3), (N'Dia a dia', 4);

INSERT dbo.Beneficio (CategoriaId, Nome, DescricaoCurta, Icone, Ordem, Ficticio)
SELECT c.Id, b.Nome, b.Descricao, b.Icone, b.Ordem, 1
FROM (VALUES
 (N'Financeiro', N'Vale-alimentação ou refeição', N'Escolha o formato que cabe melhor na sua rotina.', 'bi-credit-card', 1),
 (N'Financeiro', N'Vale-transporte', N'Ajuda no trajeto de casa até a fábrica.', 'bi-bus-front', 2),
 (N'Financeiro', N'Participação nos resultados (PPR)', N'Quando a empresa bate as metas, o time divide o resultado.', 'bi-graph-up-arrow', 3),
 (N'Financeiro', N'Descontos em produtos', N'Condições especiais na compra de produtos da empresa.', 'bi-percent', 4),
 (N'Educação', N'Auxílio educação', N'Apoio financeiro para graduação e cursos técnicos.', 'bi-mortarboard', 1),
 (N'Educação', N'Cursos e treinamentos técnicos', N'Capacitação contínua dentro da jornada de trabalho.', 'bi-journal-text', 2),
 (N'Educação', N'Formação de liderança', N'Trilha de desenvolvimento para quem quer liderar equipes.', 'bi-people', 3),
 (N'Saúde e bem-estar', N'Plano de saúde', N'Cobertura médica para você.', 'bi-hospital', 1),
 (N'Saúde e bem-estar', N'Seguro de vida', N'Proteção para você e sua família.', 'bi-umbrella', 2),
 (N'Saúde e bem-estar', N'Segurança em primeiro lugar', N'Programa contínuo de prevenção de acidentes.', 'bi-shield-check', 3),
 (N'Dia a dia', N'Refeitório no local', N'Refeições balanceadas durante o expediente.', 'bi-cup-hot', 1),
 (N'Dia a dia', N'Uniforme e EPI fornecidos', N'Equipamentos de proteção e uniforme sem custo.', 'bi-person-workspace', 2),
 (N'Dia a dia', N'Transporte fretado', N'Rotas fretadas para os principais bairros.', 'bi-truck', 3)
) b (Categoria, Nome, Descricao, Icone, Ordem)
JOIN dbo.BeneficioCategoria c ON c.Nome = b.Categoria;

/* Vagas de exemplo (fictícias) */
INSERT dbo.Vaga (Slug, Titulo, Descricao, UnidadeId, AreaId, TurnoId, TipoContratoId, ModalidadeId, ProgramaId, Status, PublicadaEm)
SELECT v.Slug, v.Titulo, v.Descricao, u.Id, a.Id, t.Id, tc.Id, m.Id, p.Id, v.Status,
       CASE WHEN v.Status IN ('PUBLICADA','PAUSADA') THEN SYSUTCDATETIME() END
FROM (VALUES
 ('operador-de-producao-timbo', N'Operador(a) de Produção', N'Atuação na linha de conformação e tratamento térmico de ferramentas em aço.',
    N'Matriz Timbó', N'Produção', N'1º turno', N'CLT', N'Presencial', NULL, 'PUBLICADA'),
 ('soldador-timbo', N'Soldador(a)', N'Soldagem de componentes e conjuntos conforme procedimentos de qualidade.',
    N'Matriz Timbó', N'Produção', N'2º turno', N'CLT', N'Presencial', NULL, 'PUBLICADA'),
 ('mecanico-de-manutencao-industrial-timbo', N'Mecânico(a) de Manutenção Industrial', N'Manutenção preventiva e corretiva de prensas, fornos e linhas de produção.',
    N'Matriz Timbó', N'Manutenção', N'3º turno', N'CLT', N'Presencial', NULL, 'PUBLICADA'),
 ('tecnico-de-metalurgia-laboratorio-timbo', N'Técnico(a) de Metalurgia - Laboratório', N'Ensaios físicos e químicos de aços e acompanhamento das propriedades mecânicas.',
    N'Matriz Timbó', N'Qualidade e Laboratório', N'Comercial (dia)', N'CLT', N'Presencial', NULL, 'PUBLICADA'),
 ('auxiliar-de-expedicao-recife', N'Auxiliar de Expedição', N'Separação, conferência e expedição de produtos no centro de distribuição.',
    N'Filial Recife', N'Logística', N'Escala 12x36', N'CLT', N'Presencial', NULL, 'PUBLICADA'),
 ('estagio-engenharia-de-materiais-timbo', N'Estágio em Engenharia de Materiais', N'Apoio a projetos de melhoria de processo e análise de materiais.',
    N'Matriz Timbó', N'Engenharia', N'Comercial (dia)', N'Estágio', N'Presencial', N'Programa de Estágio (exemplo)', 'PUBLICADA'),
 ('analista-comercial-canoas', N'Analista Comercial', N'Atendimento a clientes e representantes da região Sul.',
    N'Escritório Rio Grande do Sul', N'Comercial', N'Comercial (dia)', N'CLT', N'Híbrido', NULL, 'RASCUNHO')
) v (Slug, Titulo, Descricao, Unidade, Area, Turno, Contrato, Modalidade, Programa, Status)
JOIN dbo.Unidade u ON u.Nome = v.Unidade
JOIN dbo.Area a ON a.Nome = v.Area
JOIN dbo.Turno t ON t.Nome = v.Turno
JOIN dbo.TipoContrato tc ON tc.Nome = v.Contrato
JOIN dbo.Modalidade m ON m.Nome = v.Modalidade
LEFT JOIN dbo.Programa p ON p.Nome = v.Programa;

INSERT dbo.VagaStatusHist (VagaId, Status) SELECT Id, Status FROM dbo.Vaga;

INSERT dbo.VagaRequisito (VagaId, Texto, Obrigatorio)
SELECT g.Id, r.Texto, r.Obrig
FROM (VALUES
 ('operador-de-producao-timbo', N'Ensino médio completo', 1),
 ('operador-de-producao-timbo', N'Experiência em indústria metalúrgica', 0),
 ('soldador-timbo', N'Experiência comprovada em soldagem MIG/MAG', 1),
 ('soldador-timbo', N'Curso de soldagem', 0),
 ('mecanico-de-manutencao-industrial-timbo', N'Curso técnico em mecânica ou manutenção industrial', 1),
 ('mecanico-de-manutencao-industrial-timbo', N'Conhecimento de NR-10 e NR-12', 0),
 ('tecnico-de-metalurgia-laboratorio-timbo', N'Curso técnico em metalurgia ou química', 1),
 ('auxiliar-de-expedicao-recife', N'Ensino médio completo', 1),
 ('auxiliar-de-expedicao-recife', N'Operação de empilhadeira', 0),
 ('estagio-engenharia-de-materiais-timbo', N'Cursando engenharia de materiais ou metalúrgica', 1),
 ('analista-comercial-canoas', N'Experiência em vendas técnicas B2B', 1)
) r (Slug, Texto, Obrig)
JOIN dbo.Vaga g ON g.Slug = r.Slug;

INSERT dbo.VagaAtividade (VagaId, Texto)
SELECT g.Id, r.Texto
FROM (VALUES
 ('operador-de-producao-timbo', N'Operar equipamentos da linha conforme a ordem de produção'),
 ('operador-de-producao-timbo', N'Registrar apontamentos e inspecionar peças'),
 ('soldador-timbo', N'Executar soldas seguindo desenho e procedimento'),
 ('mecanico-de-manutencao-industrial-timbo', N'Diagnosticar falhas e executar reparos em máquinas'),
 ('tecnico-de-metalurgia-laboratorio-timbo', N'Realizar ensaios de dureza, tração e análise química'),
 ('auxiliar-de-expedicao-recife', N'Conferir e separar pedidos para despacho'),
 ('estagio-engenharia-de-materiais-timbo', N'Acompanhar ensaios e levantar dados para projetos de melhoria')
) r (Slug, Texto)
JOIN dbo.Vaga g ON g.Slug = r.Slug;

INSERT dbo.VagaDiferencial (VagaId, Texto)
SELECT g.Id, r.Texto
FROM (VALUES
 ('operador-de-producao-timbo', N'Possibilidade de crescimento para líder de turno'),
 ('soldador-timbo', N'Certificação em soldagem'),
 ('mecanico-de-manutencao-industrial-timbo', N'Experiência com prensas e fornos industriais'),
 ('tecnico-de-metalurgia-laboratorio-timbo', N'Conhecimento de aços microligados'),
 ('estagio-engenharia-de-materiais-timbo', N'Inglês técnico')
) r (Slug, Texto)
JOIN dbo.Vaga g ON g.Slug = r.Slug;

/* Beneficios: vagas CLT recebem o pacote completo abaixo; estagio recebe um pacote menor */
INSERT dbo.VagaBeneficio (VagaId, BeneficioId)
SELECT g.Id, b.Id
FROM dbo.Vaga g
JOIN dbo.TipoContrato tc ON tc.Id = g.TipoContratoId AND tc.Nome = N'CLT'
JOIN dbo.Beneficio b ON b.Nome IN (N'Vale-alimentação ou refeição', N'Vale-transporte',
     N'Participação nos resultados (PPR)', N'Plano de saúde', N'Seguro de vida',
     N'Refeitório no local', N'Uniforme e EPI fornecidos', N'Cursos e treinamentos técnicos');

INSERT dbo.VagaBeneficio (VagaId, BeneficioId)
SELECT g.Id, b.Id
FROM dbo.Vaga g
JOIN dbo.TipoContrato tc ON tc.Id = g.TipoContratoId AND tc.Nome = N'Estágio'
JOIN dbo.Beneficio b ON b.Nome IN (N'Vale-transporte', N'Refeitório no local', N'Cursos e treinamentos técnicos');

/* Conteudo institucional: sobre, qualidade e meio ambiente resumem o site da empresa;
   privacidade e diversidade sao textos de exemplo (Ficticio = 1). */
INSERT dbo.ConteudoInstitucional (Secao, Titulo, Texto, Ordem, Ficticio) VALUES
 ('sobre', N'Quem somos',
  N'Empresa brasileira de capital aberto, com mais de 80 anos de história, cerca de 1.200 colaboradores e sede em Timbó (SC). Produz ferramentas e peças em aço para diversos segmentos e exporta para mais de 60 países.', 1, 0),
 ('qualidade', N'Qualidade',
  N'Compromisso com a melhoria contínua da satisfação de clientes, fornecedores, colaboradores e acionistas, e com a preservação do meio ambiente.', 2, 0),
 ('meio-ambiente', N'Meio ambiente',
  N'Monitoramento contínuo de águas, solo e emissões, gestão de resíduos sólidos e área verde de preservação no entorno da sede.', 3, 0),
 ('diversidade', N'Diversidade e inclusão (exemplo)',
  N'Texto de exemplo: oportunidades iguais para todas as pessoas, com vagas afirmativas identificadas e processos seletivos acessíveis.', 4, 1),
 ('privacidade', N'Privacidade e LGPD (exemplo)',
  N'Texto de exemplo: seus dados pessoais são tratados conforme a Lei 13.709/2018, apenas para o processo seletivo. Para exercer seus direitos, fale com o encarregado de dados: dpo@exemplo.local.', 5, 1);
GO
