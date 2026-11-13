using Carreiras.Infra.Candidaturas;
using Carreiras.Infra.Vitrine;
using Carreiras.Web.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Carreiras.Web.Pages;

/// <summary>Candidatura espontânea (Entrega 05): o mesmo fluxo da candidatura a uma vaga, com VagaId nulo.</summary>
[RequestSizeLimit(LimiteCorpo)]
[RequestFormLimits(MultipartBodyLengthLimit = LimiteCorpo)]
public class BancoDeTalentosModel : CandidatarModel
{
    public BancoDeTalentosModel(VitrineServico vitrine, CandidaturaServico candidaturas, DesafioHumano desafio,
        ReferenciaEnvio referencia, LimitadorTentativas limite, IConfiguration config,
        RascunhoCurriculos rascunhos, ReferenciaRascunho refRascunho)
        : base(vitrine, candidaturas, desafio, referencia, limite, config, rascunhos, refRascunho) { }

    public override bool Espontanea => true;
}
