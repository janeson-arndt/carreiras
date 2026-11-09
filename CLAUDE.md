# Portal de Carreiras: briefing para o Claude Code

Projeto de estudo próprio. Portal de carreiras de uma empresa de metalurgia, com **portal público** (vagas e candidatura) e **portal administrativo do RH**. O cenário usa a Metalúrgica Timboense (Metisa) só como referência. Este arquivo resume tudo o que foi decidido na fase de análise e prototipação. Leia-o inteiro antes de escrever código.

> Idioma: toda a interface e as mensagens são em **português do Brasil**. Identificadores de código seguem o banco (português, sem acento).

## 1. Como trabalhar neste projeto

1. **Antes de codar**, confirme com o usuário as decisões da seção 11 e proponha um plano curto por entrega. Não mude uma decisão já fechada sem perguntar.
2. Trabalhe **uma entrega por vez**, na ordem da seção 7. Em cada uma, implemente, rode os testes e só então siga.
3. Os protótipos HTML são a **especificação visual e de comportamento**. Reproduza o resultado, não o código: eles usam CSS próprio e a implementação deve usar **Bootstrap 5** com os tokens da seção 8.
4. Não invente conteúdo da empresa. O que não existe no site dela é **conteúdo de exemplo** e fica marcado (`Ficticio = 1`).
5. O código-fonte fica na máquina do usuário, sem repositório remoto por enquanto. Faça commits pequenos mesmo assim.

## 2. Arquivos de referência

| Arquivo | Para que serve |
|---|---|
| `carreiras_entrega01.sql` | **Fonte de verdade do banco** (26 tabelas, índices, regras, rotina de anonimização e seed). |
| `003_candidatura_pendente.sql` | Script incremental da **Entrega 04**: colunas de candidatura pendente em `ConfirmacaoEmail` e a rotina `usp_ExpirarCandidaturasNaoConfirmadas`. Rode **depois** do 002 (pode ser reexecutado). |
| `002_usuarios.sql` | Script incremental da **gestão de usuários do RH**: campos de controle e vínculo com o ASP.NET Identity. Rode **depois** do 001. |
| `carreiras_analise.docx` | Análise completa: regras de negócio, fluxos, critérios de aceitação, riscos e glossário. |
| `prototipo_vitrine_c.html` | Portal público (Entrega 03, e a entrada do formulário da 04 e do banco de talentos da 05). **Versão escolhida.** |
| `prototipo_admin_c.html` | Portal do RH (Entregas 02 e 06). **Versão escolhida.** |
| `prototipo_vitrine.html`, `prototipo_vitrine_b.html`, `prototipo_admin.html`, `prototipo_admin_b.html` | Alternativas **descartadas**. Ignore. |

O `prototipo_vitrine_c.html` tem imagens embutidas em base64 (cerca de 600 KB). Não é preciso lê-lo inteiro: as imagens são só a foto aérea, duas capas de produto, o selo ISO 14001 e a logo, e o usuário tem os arquivos originais. Use o HTML para entender estrutura, textos e comportamento.

Para abrir um protótipo: dois cliques no arquivo. Os dados são de exemplo e as alterações valem só enquanto a página está aberta.

## 3. Stack (decisões fechadas)

- ASP.NET Core com **um único aplicativo**: área pública e área administrativa (`Admin`) no mesmo projeto, **renderização no servidor** (Razor Pages ou MVC, escolher e justificar na proposta).
- SQL Server (instância local Developer). Banco: `RH_METISA`.
- Acesso a dados: **EF Core** nas escritas e **SQL puro** (Dapper ou `FromSql`) nas consultas de listagem.
- Autenticação do RH: **ASP.NET Identity com cookie**.
- Front-end: **Bootstrap 5**, mobile-first, ícones **Bootstrap Icons** (o nome do ícone fica em `Beneficio.Icone`, por exemplo `bi-credit-card`).
- Currículos: **arquivo em disco, fora do `wwwroot`**; no banco só o metadado (`Curriculo`).
- E-mail: SMTP configurável. Em desenvolvimento, use um coletor local ou grave o e-mail em arquivo.

Estrutura sugerida (proposta, ajustável):

```
src/
  Carreiras.Web/            Razor Pages/MVC, Areas/Admin, wwwroot, Identity
  Carreiras.Dominio/        regras puras (slug, transições de status, validações, retenção)
  Carreiras.Infra/          EF Core, consultas SQL, armazenamento de arquivos, e-mail
tests/
  Carreiras.Testes/         unitários das regras + integração com SQL Server
db/                         scripts numerados (o 001 é o carreiras_entrega01.sql)
docs/                       análise e protótipos
```

## 4. Banco de dados

- **Execute `carreiras_entrega01.sql` no SSMS**, conectado à instância local e com o banco `RH_METISA` já criado e vazio. O script começa com `USE RH_METISA`.
- O script **não é reexecutável**. Para recomeçar, apague as tabelas ou recrie o banco vazio.
- Conferência: `SELECT COUNT(*) FROM dbo.Vaga;` deve devolver **7**. O seed traz 3 unidades, 7 vagas (6 publicadas e 1 rascunho), 13 benefícios em 4 categorias, 3 programas e 5 conteúdos institucionais.
- Grupos de tabelas: domínios (`Area`, `Turno`, `TipoContrato`, `Modalidade`, `Unidade`, `Programa`); vaga (`Vaga`, `VagaBeneficio`, `VagaRequisito`, `VagaAtividade`, `VagaDiferencial`, `VagaStatusHist`); benefícios (`BeneficioCategoria`, `Beneficio`); candidato e LGPD (`Candidato`, `Curriculo`, `Candidatura`, `CandidaturaStatusHist`, `ConsentimentoLgpd`, `ConfirmacaoEmail`, `SolicitacaoTitular`); administração (`PerfilRh`, `UsuarioRh`, `LogAuditoria`, `ConteudoInstitucional`, `Documento`).
- Rotina `dbo.usp_AnonimizarCandidatosExpirados`: anonimiza candidatos com `RetencaoAte` vencida. Ela troca o e-mail por `anonimizado-{id}@invalid.local` (a coluna é única e obrigatória), apaga nome, telefone, cidade, UF e IP, e marca `Curriculo.RemovidoEm`. **A aplicação apaga os arquivos do disco** depois e chama a rotina por agendamento diário.
- **Não há usuário do RH no seed.** O primeiro administrador deve ser criado pela aplicação (comando de linha, página de configuração inicial ou seed em desenvolvimento lendo a senha dos user-secrets).
- Alguns campos do banco são `NOT NULL`: `Vaga` exige título, descrição, unidade, área, turno, contrato e modalidade **mesmo em rascunho**.

### Script 002: usuários do RH

- Execute `002_usuarios.sql` **depois** do `001`, no mesmo banco. Também não é reexecutável.
- Parte A: `UsuarioRh` ganha `CriadoEm`, `UltimoAcessoEm` e `CriadoPorUsuarioRhId`.
- Parte B (**recomendada**): `UsuarioRh.IdentityUserId` (único) liga a tabela ao ASP.NET Identity, e `SenhaHash` passa a aceitar nulo. O Identity guarda credencial, bloqueio por tentativas e carimbo de segurança; `UsuarioRh` guarda o negócio (perfil, situação, histórico). As chaves estrangeiras de auditoria e histórico continuam em `UsuarioRh.Id`. Se o usuário recusar o Identity, pule a parte B e crie uma tabela de convites.
- Os dois scripts foram validados só quanto à **sintaxe** (parser T-SQL). Nenhum foi executado em SQL Server por quem os escreveu: rode, e se houver erro, corrija o script.

### Credenciais e segurança da conexão

- **Nunca use o login `sa` na aplicação** e nunca grave senha no repositório nem em `appsettings.json`. Use `dotnet user-secrets` em desenvolvimento.
- Crie um login próprio, por exemplo `carreiras_app`, com permissão apenas sobre `RH_METISA` (leitura, escrita e `EXECUTE` na rotina de anonimização).
- Formato da string, sem segredo: `Server=<instância>;Database=RH_METISA;User Id=carreiras_app;Password=<user-secrets>;TrustServerCertificate=True`.

## 5. Regras de negócio

| Regra | Descrição |
|---|---|
| RN01 | O **slug** da vaga é único e **não muda depois de publicada**. Enquanto é rascunho, acompanha título e unidade (`{titulo-normalizado}-{timbo\|recife\|canoas}`; se existir, acrescenta `-2`, `-3`). |
| RN02 | Transições de status (proposta a confirmar): RASCUNHO→PUBLICADA; PUBLICADA↔PAUSADA; PUBLICADA ou PAUSADA→ENCERRADA. Vaga encerrada não reabre: duplica-se. |
| RN03 | Só vagas **PUBLICADA** aparecem no portal público. |
| RN04 | Faixa salarial opcional e oculta por padrão. Só aparece quando `ExibirSalario = 1`. `SalarioMax` não pode ser menor que `SalarioMin`. Para marcar "mostrar", os dois valores são obrigatórios. |
| RN05 | **Candidatura avulsa**: sem conta nem senha. O candidato é identificado pelo **e-mail único**; o registro em `Candidato` nasce na primeira candidatura confirmada. |
| RN06 | Um candidato só se candidata **uma vez** a cada vaga (índice único filtrado). Candidatura espontânea (`VagaId` nulo) é permitida. |
| RN07 | Candidatura, pedido de exclusão e pedido de cópia só valem depois do clique em link com **expiração e uso único**. Guarde só o **hash** do token. |
| RN08 | Todo envio exige aceite do termo, gravado com versão, data e IP. |
| RN09 | Retenção de **12 meses**. Depois, anonimização (seção 4). O histórico de candidaturas permanece. |
| RN10 | Currículo: **PDF ou DOCX, até 5 MB**. Valide o tipo real do arquivo, não só a extensão. |
| RN11 | Pretensão salarial opcional, gravada na candidatura, visível só ao RH. |
| RN12 | **Auditoria centralizada**: toda alteração nas tabelas auditadas é gravada em `LogAuditoria` por **um único ponto** (interceptador de `SaveChanges` do EF Core). Não use `ExecuteUpdate`/`ExecuteDelete` nessas tabelas, porque eles não passam pelo interceptador. Listas (benefícios da vaga) comparam-se como **conjunto**, sem considerar a ordem. Salvar sem mudar nada **não grava log**. |
| RN13 | `ADMINISTRADOR` faz tudo. `ANALISTA` gerencia vagas e candidaturas e **só consulta** os cadastros. `LEITURA` só consulta. Benefícios, programas, conteúdo da vitrine, **usuários** e auditoria são do administrador (proposta a confirmar). |
| RN14 | Benefícios na vitrine: agrupados por categoria, na ordem cadastrada, com ícone. A cor dos cartões vem do CSS, **não do banco**. |
| RN15 | Conteúdo fictício é marcado com `Ficticio = 1` no banco. **Decisão do dono do produto (08/11/2026): a etiqueta "exemplo" foi removida das telas do RH**; a marca continua só no banco (e na caixa "Texto de exemplo" da tela de Conteúdo), e não deve ser perdida ao editar. Antes de publicar o portal, revise os itens com `Ficticio = 1` (o texto de privacidade traz um e-mail de encarregado de dados fictício). |
| RN16 | **Sempre ao menos um administrador ativo.** A aplicação bloqueia desativar ou rebaixar o último administrador que consegue entrar (conta ativa e convite já aceito). Faça a verificação e a alteração **na mesma transação**, travando as linhas dos administradores (por exemplo `UPDLOCK, HOLDLOCK` ou isolamento `Serializable`), para que duas desativações simultâneas não deixem o portal sem administrador. **Decisão tomada: não há gatilho no banco**; a regra vive só na aplicação. |
| RN17 | Ninguém desativa a si mesmo nem muda o próprio perfil. Quem faz isso é outro administrador. |
| RN18 | **Convite, sem senha.** O administrador cadastra nome, e-mail e perfil. A pessoa recebe e-mail com link de uso único e prazo para criar a própria senha. Ninguém além dela define ou vê a senha. "Enviar link de nova senha" segue o mesmo modelo. |
| RN19 | **Desativar, não excluir.** Usuário não é excluído (preserva o histórico de auditoria e de status). Desativado não entra e **perde as sessões abertas**; pode ser reativado. O e-mail é único e não pode ser trocado: desativa-se a conta e cadastra-se outra. |
| RN20 | Criação, envio de convite e de link de nova senha, mudança de perfil, ativação e desativação vão para `LogAuditoria`, **nunca com senha, hash ou link**. |

Aplique as permissões **no servidor** (política/atributo por ação), não só escondendo botões. O protótipo faz as duas coisas.

## 6. Mapa de telas e rotas

**Público** (`prototipo_vitrine_c.html`):

| Tela | Rota sugerida |
|---|---|
| Início: hero, faixa de unidades, lista de vagas com filtros, história, benefícios, programas, diversidade, privacidade | `/` |
| Lista de vagas com filtros (unidade, área, turno, contrato), estado vazio com "Entrar no banco de talentos" | `/vagas` |
| Página da vaga | `/vagas/{slug}` (vaga inexistente ou não publicada: página "Vaga não encontrada") |
| Candidatura (formulário) e "Falta um passo" | a partir da página da vaga; confirmação em `/confirmar/{token}` |
| Banco de talentos e "Meus dados" (exclusão ou cópia) | `/banco-de-talentos`, `/meus-dados` |

**Administrativo** (`prototipo_admin_c.html`), sob `/admin`: `vagas`, `vagas/nova`, `vagas/{id}`, `candidaturas`, `beneficios`, `programas`, `conteudo`, `usuarios`, `auditoria`.

## 7. Entregas, ordem e critérios de aceitação

| # | Entrega | Referência |
|---|---|---|
| 01 | Modelagem do banco, LGPD e auditoria | `carreiras_entrega01.sql` (**pronta**; falta executar e conferir) |
| 02 | Portal administrativo do RH | `prototipo_admin_c.html` |
| 03 | Portal público: vitrine, listagem e página da vaga | `prototipo_vitrine_c.html` |
| 04 | Candidatura avulsa com confirmação por e-mail | formulário do `prototipo_vitrine_c.html` |
| 05 | Banco de talentos e direitos do titular | entrada no `prototipo_vitrine_c.html`; **"Meus dados" sem protótipo** |
| 06 | Gestão de candidaturas pelo RH | tela Candidaturas do `prototipo_admin_c.html` |

As Entregas 01 a 04 formam o MVP funcional. Critérios por entrega:

**01.** O script roda em banco vazio sem erro. O seed cria os totais da seção 4. O banco rejeita segunda candidatura do mesmo candidato à mesma vaga. A rotina de anonimização troca o e-mail e mantém as candidaturas.

**02.**
- Só usuário autenticado acessa `/admin`. `LEITURA` não cria, edita nem muda status.
- Vaga em rascunho não aparece no portal público.
- Cada mudança de status grava `VagaStatusHist` e `LogAuditoria`. Cada edição de campo grava log com usuário, tabela, campo, valor antigo e novo.
- Duplicar cria rascunho com slug novo e único. Vaga encerrada abre bloqueada.
- ~~Itens de exemplo exibem a etiqueta "exemplo".~~ Removido a pedido do dono do produto (ver RN15).
- Salvar sem alteração não grava log. Os campos obrigatórios valem também para o rascunho.
- O menu **Ações** abre pelo teclado, fecha com Esc devolvendo o foco ao botão, e Encerrar aparece separado e em vermelho.
- **Usuários** (só `ADMINISTRADOR`): lista com situação (Ativo, Convite pendente, Desativado), busca e filtro. O administrador cadastra usuário com nome, e-mail e perfil, **inclusive outro administrador**; a pessoa recebe convite e o formulário **não tem campo de senha**.
- O administrador desativa e reativa usuários; o desativado não consegue entrar e perde as sessões abertas. Também reenvia convite e envia link de nova senha.
- Ninguém desativa nem muda o próprio perfil. A ação sobre o último administrador é bloqueada com a explicação visível.
- Criação, convite, mudança de perfil, ativação e desativação ficam na auditoria, sem nenhum dado de senha.

**03.**
- Só vagas PUBLICADA na lista. Filtros combináveis. URL `/vagas/{slug}`, estável após publicar.
- Benefícios agrupados por categoria, sem carrossel.
- A página da vaga mostra requisitos, atividades, diferenciais, benefícios, unidade e turno. A faixa salarial só aparece com `ExibirSalario = 1`.
- Contraste WCAG AA e navegação por teclado. Marcação **JobPosting** (JSON-LD).
- Toda imagem tem texto alternativo, inclusive o texto que está dentro das artes. O rodapé tem a logo e links para Facebook, Instagram e LinkedIn em nova aba.

**04.**
- Obrigatórios: nome, e-mail, telefone, cidade/UF e currículo; sem aceite do termo não envia.
- Arquivo fora de PDF/DOCX ou acima de 5 MB é rejeitado com mensagem clara.
- A candidatura só é efetivada após o clique no link. O link expira e é de uso único.
- Segunda candidatura do mesmo e-mail à mesma vaga é bloqueada com mensagem.
- Consentimento gravado com versão do termo, data e IP. Captcha simples contra envio automático.

**05.** Candidatura espontânea cria `Candidatura` com `VagaId` nulo. Pedido de exclusão ou cópia só executa após o clique no link e gera `SolicitacaoTitular`. O job anonimiza vencidos e depois do job o e-mail original pode ser reutilizado. Os arquivos dos anonimizados são apagados do disco.

**06.** Lista por vaga com filtro de status; o RH abre o currículo; mudança de status grava histórico com usuário; anonimizados aparecem como "Anonimizado"; `LEITURA` não muda status; espontâneas numa visão separada.

## 8. Design e interface

**Tokens** (cores da comunicação da empresa e derivadas):

| Token | Valor | Uso |
|---|---|---|
| Verde da marca (`--c-verde`) | `#019641` | **Decisão do dono do produto (08/11/2026): é o verde da logo.** Botões primários, barras, bordas, aro do avatar, borda do login, nome da marca (19px em negrito = texto grande). Hover do botão `#017A35`. **Não use como cor de texto pequeno**: sobre branco dá 3,86:1. |
| Verde de texto (`--c-verde-texto`) | `#018138` | Links e texto verde pequeno (5,0:1 no branco e 4,54:1 no fundo da página). Hover de link e texto sobre verde claro: `#004D1F`. |
| ~~Verde escuro `#006729` e verde `#118F40`~~ | | Substituídos pelos dois acima. |

> **Exceção de acessibilidade conhecida:** o rótulo branco dos botões primários sobre `#019641` dá 3,86:1, abaixo dos 4,5:1 do WCAG AA para texto normal (o axe-core aponta `color-contrast` em `.btn-primary`). Foi uma escolha explícita do dono do produto. `ContrasteTestes` registra o piso de 3:1 e exige 4,5:1 no hover. Para voltar a AA, basta usar `#01893B` ou mais escuro no fundo do botão.
| Cinza | `#717171` | Bordas de campos e cartões. Texto secundário usa `#5C5C5C`. |
| Fundo | `#F3F4F3` | Faixas e blocos de apoio. |
| Verde claro | `#E4F1E8` | Cartões claros, etiquetas, item ativo do menu. |
| Texto | `#232323` | Texto principal. |
| Erro | `#B3261E` | Validação. Fundo de erro `#FBE9E7`. |

Fontes: **Exo 2** (títulos, pesos 600/700/800) e **Source Sans 3** (texto, 400/600, corpo de 17 px no público). Defina os tokens como variáveis CSS e sobrescreva as variáveis do Bootstrap.

**Princípios que o usuário aprovou** (mantenha):

- O verde é **contido**: botões principais, marca do item ativo, selos e links. Título de página em cinza escuro, botões secundários em cinza neutro. O usuário achou o menu lateral todo verde "gritante".
- Portal do RH: menu lateral **claro**, marca "Metalúrgica Timboense" em verde, **conta do usuário na barra do topo** (iniciais, nome, perfil, menu com e-mail e Sair), e nas listas **um botão principal (Editar) mais um menu Ações** (Pausar/Reativar/Publicar, Duplicar, e Encerrar separado e em vermelho). Nada de quatro botões iguais por linha.
- Vitrine pública: faixa de unidades com contagem de vagas funcionando como filtro; benefícios em **grade que quebra linha** (no celular, rolagem horizontal com encaixe), **sem carrossel**; botão "Candidatar-se" fixo no rodapé no celular.
- **Usuários**: mesma estrutura das outras listas (botão principal **Editar** e menu **Ações**). No menu, **Desativar** fica no fim, separado e em vermelho. Quando a ação é bloqueada, o item continua visível, marcado com `aria-disabled`, com o **motivo escrito logo abaixo** ("Você não pode desativar a si mesmo.", "É o único administrador ativo. Cadastre outro administrador antes de desativar este."). O diálogo de novo usuário avisa: "A pessoa recebe um e-mail com um link para criar a senha. O link expira e só pode ser usado uma vez. Você não vê nem define a senha." O item "Simular aceite do convite" existe **só no protótipo**.
- Texto da interface: frases curtas, verbos claros, "sentence case". Erro explica o que fazer, sem pedir desculpas (por exemplo, "Informe um e-mail válido, como nome@exemplo.com.").
- Estados vazios convidam à ação (por exemplo, "Nenhuma vaga aberta com esses filtros" com botão para o banco de talentos).

**Acessibilidade (mínimo WCAG AA):** foco visível, rótulos em todos os campos, `aria-invalid` e mensagem por campo mais resumo de erros com links, `aria-live` nas contagens e avisos, `prefers-reduced-motion`, alvos de toque confortáveis, texto alternativo descrevendo também o texto dentro das imagens. Menus de ação seguem o padrão **disclosure** (`aria-expanded`, `aria-controls`, Esc fecha e devolve o foco, clique fora fecha, só um aberto por vez, posição ajustada para não sair da tela).

## 9. Segurança e privacidade

- Currículo é dado pessoal (LGPD). Termo versionado, retenção de 12 meses, canal do titular (Entrega 05).
- Upload: valide tipo real e tamanho, **renomeie** o arquivo, salve fora do `wwwroot` e sirva **somente a usuário autenticado** do RH. Considere varredura de malware se o ambiente permitir.
- Token de confirmação: aleatório forte, guardar apenas o hash, expiração, uso único.
- `AntiForgery` em todos os formulários, HTTPS, cabeçalhos de segurança, limitação de tentativas nos endpoints públicos de envio.
- Logs não devem conter dados pessoais do candidato.
- **Usuários do RH**: use convite e link de nova senha de uso único, com expiração (tokens do Identity). Política de senha forte, bloqueio por tentativas e confirmação de e-mail. Ao desativar, **atualize o carimbo de segurança** do Identity para encerrar as sessões abertas, e valide o carimbo a cada requisição (ou em intervalo curto). Nunca registre senha, hash ou link na auditoria.
- Mantenha um **procedimento de recuperação** para o caso de ficar sem administrador (comando de linha que cria um administrador). As regras RN16/RN17, com a verificação em transação, reduzem o risco, mas não o eliminam.
- O e-mail do encarregado de dados no seed é **fictício** (`dpo@exemplo.local`). Não aponte para o endereço real da empresa.
- **Logo, fotos e nome da Metisa** são da empresa e servem só para avaliação em ambiente local. Não publique o portal com eles.

## 10. Testes

- **Unitários** das regras do domínio: geração de slug e unicidade, transições de status (RN02), validação de salário (RN04), prazo de retenção, elegibilidade a candidatura única.
- **Integração** com SQL Server real (não banco em memória): índice único filtrado de candidatura, rotina de anonimização, interceptador de auditoria (inclusive "salvar sem mudança não grava log" e "lista como conjunto").
- **Usuários**: o formulário de cadastro não tem campo de senha; ninguém desativa a si mesmo; não se desativa nem se rebaixa o último administrador (inclusive com uma sessão de outro usuário); **duas desativações simultâneas de administradores não deixam o portal sem administrador** (teste de concorrência com SQL Server real); o desativado não entra e, reativado, volta a entrar; convite pendente não conta como administrador que consegue entrar; não existe ação de excluir; a auditoria nunca contém senha ou hash; salvar sem mudança não grava log.
- **Autorização**: para cada ação do portal, um teste por perfil (`ADMINISTRADOR`, `ANALISTA`, `LEITURA`) confirmando que o servidor bloqueia.
- **Acessibilidade**: verificação automática de contraste e rótulos nas telas principais.
- Os protótipos foram verificados com checagens automáticas de comportamento (35 na vitrine e 131 no portal do RH). Use esses comportamentos como casos de teste.

## 11. Decisões pendentes (pergunte ao usuário antes de começar)

1. **Fonte de verdade do esquema.** Existe o script SQL **e** a decisão de usar EF Core com migrations. Recomendação: o script é a linha de base; faça scaffold do `DbContext` a partir do banco, crie uma migração inicial vazia e use migrations só para mudanças futuras. Confirmar.
2. **RN02**: confirmar as transições de status da vaga.
3. **Identity como repositório de credenciais.** A gestão de usuários (convite, nova senha, bloqueio, encerrar sessões ao desativar) é exatamente o que o Identity já faz, então a recomendação é usá-lo, com `UsuarioRh` ligado por `IdentityUserId` (parte B do `002_usuarios.sql`). Confirmar.
4. **RN13**: o `ANALISTA` pode editar benefícios, programas e conteúdo, ou só consultar (como no protótipo)?
5. **Status da candidatura**: pode mudar para qualquer um ou há ordem? (o protótipo permite qualquer mudança.)
6. **Rascunho com menos campos**: hoje exige os mesmos campos da publicação por causa do `NOT NULL`. Se o usuário quiser rascunho "solto", o esquema precisa mudar.
7. Auditar também as alterações do próprio candidato? (hoje não.)
8. Nomes dos ícones do Bootstrap Icons usados no seed foram escritos de memória: conferir se existem.
9. CEP do escritório de Canoas está nulo (o site da empresa publica um valor com 7 dígitos, inválido).
10. Ambiente: Docker ou instância local? (hoje, instância local Developer.)

## 12. Primeiros passos sugeridos

1. Ler este arquivo e abrir os dois protótipos escolhidos.
2. Conferir que o banco foi criado pelo script (`COUNT = 7`) e que o `002_usuarios.sql` foi executado depois. Se não, orientar o usuário a executá-los, nessa ordem.
3. Fazer as perguntas da seção 11 e propor a estrutura da solução e o plano da **Entrega 02**.
4. Criar a solução, configurar Identity, o `DbContext`, o interceptador de auditoria e as políticas de perfil **antes** das telas.
5. Implementar a Entrega 02 e rodar os testes da seção 10 antes de partir para a 03.

## 13. Como rodar os testes (adicionado na Entrega 02)

Os testes de integração usam SQL Server real e **recriam o banco `RH_METISA_TESTE`** a cada execução a partir de `db/001`, `db/002` e `db/003` (o `RH_METISA` de desenvolvimento nunca é tocado). Defina a conexão com permissão de criar banco (não grave em arquivo):

```bash
export CARREIRAS_TESTE_ADMIN_CS='Server=<instância>;User Id=<login com permissão>;Password=<senha>;TrustServerCertificate=True'
dotnet test tests/Carreiras.Testes
```

- `sqlcmd` precisa de `-I` (QUOTED_IDENTIFIER ligado) para rodar os scripts 001/002 por linha de comando; o SSMS já liga.
- `dotnet ef` usa a variável `CARREIRAS_MIGRATION_CS` (conexão com DDL) via `IDesignTimeDbContextFactory`; há dois contextos: `AppDbContext` (negócio, migração inicial vazia) e `AuthDbContext` (Identity).
- Primeiro administrador / recuperação: `dotnet user-secrets set "Admin:Senha" "<senha>" --project src/Carreiras.Web` e `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Carreiras.Web -- criar-admin "Nome" email@exemplo.com`.

### Acessibilidade (adicionado na Entrega 02)

- `tests/Carreiras.Testes/Acessibilidade/`: `ContrasteTestes` (razão de contraste calculada do `carreiras.css`, 4,5:1 texto e 3:1 bordas), `EstruturaTestes` (21 telas do RH: idioma, título, h1 único, rótulos, tabelas com legenda e `scope`, ARIA válido, menus disclosure, avisos, áreas roláveis com teclado) e `VerificadorTestes` (controles negativos: HTML com defeito tem de ser reprovado).
- O que depende de layout (contraste real, reflow no celular) foi conferido com **axe-core 4.10** no navegador, em 375 px e em desktop, com zero violações. Para repetir: `npm i axe-core` fora do repositório, copiar `axe.min.js` para um `wwwroot/lib/axe-temp/` temporário (a CSP só aceita scripts do próprio site), injetar com `<script src>` e rodar `axe.run()`; **remover a pasta antes do commit**.
- Os únicos itens "incompletos" do axe são `<select>`/`<textarea>` (seta do Bootstrap é imagem de fundo); o texto é `#232323` sobre branco (16:1).
- Listas do RH viram cartões empilhados abaixo de 768 px (`.tabela-cartoes` + `data-rotulo`, com papéis ARIA para manter a semântica de tabela). A Auditoria mantém tabela rolável, com `tabindex="0"`, `role="region"` e `aria-label`.

## 14. Status da Entrega 02 (revisão contra os critérios)

**Atendidos, com evidência automática ou verificada no navegador:** acesso só autenticado e por perfil; auditoria de status e de cada campo (usuário, tabela, campo, antigo, novo); duplicar e encerrada bloqueada; salvar sem mudança não grava log (inclusive com CRLF do navegador); campos obrigatórios no rascunho; menu Ações pelo teclado (Enter/Espaço abre, Esc devolve o foco, Tab para fora fecha, Encerrar separado e em vermelho); Usuários (lista, busca, filtro, cadastro sem senha, convite de uso único com prazo, reenvio, nova senha, desativar/reativar com sessões encerradas, bloqueio do próprio e do último administrador com a explicação visível, concorrência); auditoria sem senha, hash, link ou token; Benefícios, Programas, Conteúdo e Auditoria (RN13); acessibilidade (seção 13).

**Desvios do protótipo (aceitos):** "Novo usuário" é uma página, não um diálogo.

**Pendências conhecidas, deliberadas:**
- "Vaga em rascunho não aparece no portal público" só pode ser verificado na Entrega 03 (RN03 vira teste lá).
- A tela de Conteúdo não tem anexo de `Documento` (a tabela existe).
- ~~`Vaga` não tem coluna `Ficticio`~~: deixou de ser pendência, pois a etiqueta "exemplo" foi removida das telas.
- Antes de publicar em HTTPS: `Cookie.SecurePolicy` está `SameAsRequest`; trocar para `Always`. O SMTP real ainda não foi configurado (em desenvolvimento o e-mail vai para `storage/emails`).
- Decisão nº 8 do briefing fechada: os 13 ícones do seed existem no Bootstrap Icons 1.11.3.

### Endereço do portal (PathBase)

O portal é servido sob um prefixo configurável: `PathBase` (variável de ambiente ou configuração). O perfil `http` do `launchSettings.json` já define `PathBase=/rhmetisa`, então `dotnet run --project src/Carreiras.Web --launch-profile http` serve em **http://localhost:5129/rhmetisa/admin**. Com o prefixo ligado, **só** ele vale: `/` redireciona para o portal e qualquer outro caminho sem o prefixo dá 404. Sem `PathBase` (como nos testes), o portal fica na raiz. Nunca escreva caminho fixo em código novo: use `asp-page`, `Url.Page`, `Url.Content("~/...")` e `RedirectToPage`, que já incluem o prefixo (coberto por `PathBaseTestes`).

## 15. Status da Entrega 03 (portal público)

**Rotas:** `/` (início), `/vagas` (lista com filtros), `/vagas/{slug}` (vaga; inexistente ou não publicada = 404 "Vaga não encontrada"), `/candidatar?vaga={slug}` e `/banco-de-talentos` (**páginas de "em breve"**: o formulário é a Entrega 04 e o banco de talentos, a 05). Sob o prefixo configurado, tudo fica em `/rhmetisa/...`.

**Como é feito:** consultas em SQL puro (`VitrineServico`, Dapper); filtros por formulário GET (um por grupo, sem JavaScript obrigatório); layout público próprio (`_LayoutPublico`, `vitrine.css`, 17px); ícones dos benefícios pelo nome gravado no banco (`bi-…`, Bootstrap Icons 1.11.3 hospedado em `wwwroot/lib`); JSON-LD `JobPosting` em `JobPostingJsonLd`; textos editáveis vêm do cadastro de Conteúdo (sobre, qualidade, meio-ambiente, diversidade, privacidade); o restante da seção "Nossa história" (números, fotos, compromissos) é texto fixo portado do protótipo.

**Atendido, com teste:** só `PUBLICADA` aparece (lista, filtros, contagens e URL), inclusive o ciclo publicar → pausar → republicar no mesmo endereço (RN03, pendência da Entrega 02); filtros combináveis e que se mantêm entre si; faixa salarial só com `ExibirSalario = 1` e os dois valores (também na marcação para buscadores); página da vaga com requisitos, atividades, diferenciais, benefícios, unidade e turno; benefícios agrupados por categoria na ordem cadastrada, só os ativos, sem carrossel; JSON-LD válido e à prova de injeção (título com `</script>`); toda imagem com `alt` (as artes repetem o texto que está dentro delas); rodapé com logo, unidades e 3 redes sociais em nova aba (`noopener noreferrer`); verificação de estrutura e axe-core (desktop e 375px) sem violações, exceto a abaixo.

**Exceção conhecida (decisão do dono do produto):** botão primário branco sobre `#019641` (3,86:1), como no portal do RH. Um teste (`Vitrine_usa_o_verde_da_marca_como_texto_so_em_tamanho_grande…`) garante que o verde da marca só vira cor de texto em tamanho grande e que texto branco só vai sobre o verde escuro.

**Pendências deliberadas:**
- `/candidatar` e `/banco-de-talentos` ainda não recebem currículo (Entregas 04 e 05).
- A etiqueta "conteúdo de exemplo" **continua na vitrine** para benefícios, programas e textos com `Ficticio = 1` (não há tela que limpe a marca de benefícios e programas; só a de Conteúdo tem a caixa). Antes de publicar de verdade, decidir se cada item é definitivo.
- O aviso "Ambiente de avaliação" e a nota sobre logo e fotos da Metisa dependem de `Vitrine:AvisoAvaliacao` em `appsettings.json`. **Desligar só depois de trocar imagens e logo por material liberado** (o briefing proíbe publicar com eles).
- Os links das redes sociais vieram do protótipo e não foram conferidos no ar. O JSON-LD não passou pelo Teste de Resultados Enriquecidos do Google (precisa de URL pública).
- Teclado conferido por estrutura (elementos nativos, link para pular o conteúdo, âncoras válidas); não houve teste manual com leitor de tela.

## 16. Status da Entrega 04 (candidatura avulsa com confirmação por e-mail)

**Rotas:** `/candidatar?vaga={slug}` (formulário), `/candidatar/enviada` ("Falta um passo"), `/confirmar/{token}` (link do e-mail) e `/termo-de-privacidade`. Vaga inexistente ou não publicada = 404. A candidatura espontânea (sem vaga) fica para a Entrega 05; o serviço já aceita `VagaId` nulo.

**Fluxo:** o envio cria (ou reaproveita) o `Candidato` com `EmailConfirmado = 0`, guarda o currículo, grava o consentimento (versão do termo, data e IP) e gera um token de 256 bits; o banco guarda **só o hash SHA-256**. Os dados que esperam o clique (vaga, currículo, pretensão, perfil informado) ficam em colunas novas de `ConfirmacaoEmail` (db/003). A `Candidatura` (RECEBIDA + histórico) só nasce no clique; nesse momento o perfil informado passa a valer, a retenção é estendida em 12 meses e os dados pessoais pendentes são apagados da confirmação. Quando o candidato já existe, o perfil dele **não é sobrescrito** antes da confirmação.

**Desvios deliberados:**
- **GET no link só mostra a candidatura e um botão; quem efetiva é o POST.** Antivírus e prévias de e-mail abrem links sozinhos e gastariam o uso único.
- Reenvio ("Pedir novo link") gera token novo e invalida o anterior (mensagem "link substituído"); intervalo mínimo de 60 s e no máximo 5 links por hora por candidato.
- `ArquivoCurriculo` confere o tipo real: `%PDF-` no início do PDF; DOCX precisa ser ZIP com `[Content_Types].xml` e `word/document.xml`. Nome gravado em disco = guid + extensão; o nome original só serve de rótulo para o RH.

**Segurança:** currículos em `Curriculos:Pasta` (padrão `storage/curriculos`, ignorado pelo git, **fora do wwwroot**; nada é servido por URL; o RH autenticado abrirá pelo `ArmazenamentoCurriculos.Abrir` na Entrega 06); antifalsificação do Razor Pages em todos os POST; limite de tentativas por IP (`RateLimit:Tentativas`/`JanelaMinutos`, padrão 10 em 10 min, resposta 429); captcha de soma assinado com DataProtection (validade de 1 h) mais campo-isca; o log não traz e-mail, nome nem token. O link do e-mail usa `Site:Origem` quando configurado (**configure em produção**, para não montar o link a partir do cabeçalho Host).

**Retenção:** `RetencaoServico` roda `usp_ExpirarCandidaturasNaoConfirmadas` (descarta pendências sem confirmação após 7 dias), `usp_AnonimizarCandidatosExpirados` e apaga do disco os arquivos de `Curriculo.RemovidoEm`. O job diário (`RetencaoDiaria`) fica **desligado por padrão** (`Retencao:Habilitado`); o perfil `http` do launchSettings liga. Em produção, ligar a configuração ou agendar a execução.

**Atendido, com teste (61 novos; suíte em 298):** todos os critérios da Entrega 04 (campos obrigatórios, aceite do termo, PDF/DOCX até 5 MB com tipo real, só vale após o clique, link vencido e de uso único inclusive com cliques simultâneos, duplicidade por e-mail+vaga com mensagem, consentimento com versão/data/IP, captcha e isca, 429, antifalsificação, vaga encerrada entre envio e clique, limpeza de não confirmados e do arquivo, verificação de estrutura e axe-core em desktop e 375 px). Verificado também no navegador, do envio ao clique, com o e-mail gravado em `storage/emails`.

**Exceção conhecida:** botão primário branco sobre `#019641` (3,86:1), a mesma das entregas anteriores.

**Pendências deliberadas:**
- **Texto do termo de privacidade é um texto-base sem revisão jurídica** (aviso na própria página até `Privacidade:RevisadoPeloJuridico = true`); e-mail do encarregado fictício (`Privacidade:EmailEncarregado`, padrão `dpo@exemplo.local`). Mudou o texto, mude `TermoPrivacidade.VersaoAtual`.
- Atrás de proxy reverso, configurar cabeçalhos encaminhados: o limite de tentativas e o IP do consentimento usam o endereço da conexão.
- Antes de HTTPS e produção: SMTP real (hoje o e-mail vai para arquivo), `Cookie.SecurePolicy = Always`, `Site:Origem`.
- Currículo só é aberto pelo RH na Entrega 06; banco de talentos, "Meus dados" e candidatura espontânea são da Entrega 05.


## 17. Status da Entrega 05 (banco de talentos e direitos do titular)

**Rotas:** `/banco-de-talentos` (candidatura espontânea, mesmo formulário e mesmo fluxo da 04, com `VagaId` nulo; `/candidatar` sem vaga redireciona para cá), `/meus-dados` (pedido de cópia ou exclusão) e `/meus-dados/confirmar/{token}`. Rodapé público com links para termo de privacidade, Meus dados e banco de talentos.

**Como é feito:** o formulário virou o parcial `_FormCandidatura`, usado por `CandidatarModel` e por `BancoDeTalentosModel` (herda e liga `Espontanea`). Uma pessoa tem no máximo uma entrada no banco de talentos; pode, além dela, se candidatar a vagas. `TitularServico` abre a `SolicitacaoTitular` (ABERTA), manda o link (token de 256 bits, só o hash no banco, `ConfirmacaoEmail` com Finalidade COPIA ou EXCLUSAO, 24 h) e, no clique, marca CONFIRMADA, executa e marca ATENDIDA com a data. Novo pedido do mesmo tipo reaproveita a solicitação aberta e invalida o link anterior.
- **Cópia:** e-mail em texto com cadastro, candidaturas (vaga ou banco de talentos, situação, pretensão), currículos (nome, tamanho, data, sem o arquivo) e aceites do termo (versão, data, IP). Se o e-mail falhar, o link não é consumido.
- **Exclusão:** `dbo.usp_AnonimizarCandidato` (db/004): marca currículos como removidos, apaga o IP dos aceites e as confirmações, troca e-mail por `anonimizado-{id}@invalid.local` e zera nome, telefone, cidade e UF. As candidaturas ficam. O arquivo é apagado do disco na hora. O e-mail original fica livre para novo cadastro.
- **Retenção:** igual à 04 (`RetencaoServico`/`RetencaoDiaria`); cobre os critérios de anonimização, de candidaturas mantidas e de arquivos apagados.

**Decisões:** GET no link só mostra o pedido e o POST executa (mesma razão da 04); a tela de pedido responde igual exista ou não o e-mail (não revela quem é candidato); no máximo 3 pedidos por hora por e-mail, os demais são ignorados em silêncio; sem confirmação o pedido não faz nada.

**Atendido, com teste (13 novos; suíte em 312):** os seis critérios da Entrega 05, mais antifalsificação, captcha, links vencidos/substituídos e verificação de estrutura. Navegador: axe-core sem violações novas em `/banco-de-talentos` e `/meus-dados` (só o botão primário, exceção já conhecida).

**Pendências deliberadas:** o texto do termo ainda não menciona o banco de talentos nem o canal "Meus dados" de forma detalhada (revisão jurídica); a cópia vai em texto, sem anexar o currículo; o RH só vê candidaturas espontâneas na Entrega 06 (visão própria).

## 18. Status da Entrega 06 (gestão de candidaturas pelo RH)

**Rotas (área Admin, exigem login):** `/admin/candidaturas` (lista com filtros de vaga e situação, 50 por página), `/admin/candidaturas/{id}` (detalhe com histórico) e `/admin/candidaturas/{id}?handler=Curriculo` (download). Item "Candidaturas" no menu; na lista de vagas, o número de candidaturas virou link para a lista filtrada pela vaga.

**Como é feito:** `CandidaturaRhServico` (EF, sem SQL puro) e `StatusCandidatura` (RECEBIDA, EM_ANALISE, ENTREVISTA, APROVADA, REPROVADA; o RH troca livremente, como no protótipo). A mudança de situação atualiza `Candidatura.Status` e grava `CandidaturaStatusHist` (usuário e data) na mesma transação; mesma situação ou valor inválido é recusado sem histórico. A lista tem filtro "Candidaturas espontâneas" (visão separada, `VagaId` nulo) e mostra "Anonimizado" no lugar do nome. O detalhe de anonimizado não mostra e-mail, telefone nem cidade, e o currículo não existe mais.

**Currículo:** só usuário do RH autenticado abre (qualquer perfil, inclusive LEITURA, que consulta). Sai como anexo, com o tipo pelo nome gravado (.pdf/.docx), `Cache-Control: no-store`, e só pelo nome gerado (guid) validado por padrão em `ArmazenamentoCurriculos`; caminho adulterado, arquivo ausente, anonimizado ou removido dá 404. O log registra candidatura e usuário do RH, sem dado do candidato.

**Perfis:** ADMINISTRADOR e ANALISTA mudam situação; LEITURA vê tudo, mas a lista e o detalhe não têm seletor, e o POST forçado vai para "acesso negado" (checado no servidor).

**Atendido, com teste (15 novos; suíte em 327):** os seis critérios da Entrega 06, mais login obrigatório, antifalsificação, pretensão salarial visível só ao RH e verificação de estrutura. Navegador: axe-core sem violações novas (só o botão primário, exceção conhecida); troca de situação e download conferidos com dados de exemplo, depois removidos.

**Pendências deliberadas:** não há busca por nome nem ordenação (a lista vem da mais recente); perguntas eliminatórias por vaga ficam fora (análise, seção 11); candidaturas não passam pela tela de Auditoria (o histórico de situação cumpre o papel); o download usa o rótulo do nome original do arquivo como nome sugerido.
