using System;
using System.Collections.Generic;
using Carreiras.Infra.Dados.Entidades;
using Microsoft.EntityFrameworkCore;

namespace Carreiras.Infra.Dados;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Area> Areas { get; set; }

    public virtual DbSet<Beneficio> Beneficios { get; set; }

    public virtual DbSet<BeneficioCategoria> BeneficioCategoria { get; set; }

    public virtual DbSet<Candidato> Candidatos { get; set; }

    public virtual DbSet<Candidatura> Candidaturas { get; set; }

    public virtual DbSet<CandidaturaStatusHist> CandidaturaStatusHists { get; set; }

    public virtual DbSet<ConfirmacaoEmail> ConfirmacaoEmails { get; set; }

    public virtual DbSet<ConsentimentoLgpd> ConsentimentoLgpds { get; set; }

    public virtual DbSet<ConteudoInstitucional> ConteudoInstitucionals { get; set; }

    public virtual DbSet<Curriculo> Curriculos { get; set; }

    public virtual DbSet<Documento> Documentos { get; set; }

    public virtual DbSet<LogAuditoria> LogAuditoria { get; set; }

    public virtual DbSet<Modalidade> Modalidades { get; set; }

    public virtual DbSet<PerfilRh> PerfilRhs { get; set; }

    public virtual DbSet<Programa> Programas { get; set; }

    public virtual DbSet<SolicitacaoTitular> SolicitacaoTitulars { get; set; }

    public virtual DbSet<TipoContrato> TipoContratos { get; set; }

    public virtual DbSet<Turno> Turnos { get; set; }

    public virtual DbSet<Unidade> Unidades { get; set; }

    public virtual DbSet<UsuarioRh> UsuarioRhs { get; set; }

    public virtual DbSet<Vaga> Vagas { get; set; }

    public virtual DbSet<VagaAtividade> VagaAtividades { get; set; }

    public virtual DbSet<VagaDiferencial> VagaDiferencials { get; set; }

    public virtual DbSet<VagaRequisito> VagaRequisitos { get; set; }

    public virtual DbSet<VagaStatusHist> VagaStatusHists { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Area>(entity =>
        {
            entity.Property(e => e.Ativo).HasDefaultValue(true, "DF_Area_Ativo");
        });

        modelBuilder.Entity<Beneficio>(entity =>
        {
            entity.Property(e => e.Ativo).HasDefaultValue(true, "DF_Beneficio_Ativo");

            entity.HasOne(d => d.Categoria).WithMany(p => p.Beneficios)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Beneficio_Categoria");
        });

        modelBuilder.Entity<Candidato>(entity =>
        {
            entity.HasIndex(e => e.RetencaoAte, "IX_Candidato_Retencao").HasFilter("([AnonimizadoEm] IS NULL)");

            entity.Property(e => e.CriadoEm).HasDefaultValueSql("(sysutcdatetime())", "DF_Candidato_CriadoEm");
            entity.Property(e => e.RetencaoAte).HasDefaultValueSql("(dateadd(month,(12),sysutcdatetime()))", "DF_Candidato_Retencao");
            entity.Property(e => e.Uf).IsFixedLength();
        });

        modelBuilder.Entity<Candidatura>(entity =>
        {
            entity.HasIndex(e => new { e.CandidatoId, e.VagaId }, "UX_Candidatura_Candidato_Vaga")
                .IsUnique()
                .HasFilter("([VagaId] IS NOT NULL)");

            entity.Property(e => e.CriadaEm).HasDefaultValueSql("(sysutcdatetime())", "DF_Candidatura_CriadaEm");
            entity.Property(e => e.Status).HasDefaultValue("RECEBIDA", "DF_Candidatura_Status");

            entity.HasOne(d => d.Candidato).WithMany(p => p.Candidaturas)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Candidatura_Candidato");

            entity.HasOne(d => d.Curriculo).WithMany(p => p.Candidaturas)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Candidatura_Curriculo");

            entity.HasOne(d => d.Vaga).WithMany(p => p.Candidaturas).HasConstraintName("FK_Candidatura_Vaga");
        });

        modelBuilder.Entity<CandidaturaStatusHist>(entity =>
        {
            entity.Property(e => e.AlteradoEm).HasDefaultValueSql("(sysutcdatetime())", "DF_CandStatusHist_Em");

            entity.HasOne(d => d.Candidatura).WithMany(p => p.CandidaturaStatusHists).HasConstraintName("FK_CandStatusHist_Candidatura");

            entity.HasOne(d => d.UsuarioRh).WithMany(p => p.CandidaturaStatusHists).HasConstraintName("FK_CandStatusHist_Usuario");
        });

        modelBuilder.Entity<ConfirmacaoEmail>(entity =>
        {
            entity.HasOne(d => d.Candidato).WithMany(p => p.ConfirmacaoEmails)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ConfirmacaoEmail_Candidato");
        });

        modelBuilder.Entity<ConsentimentoLgpd>(entity =>
        {
            entity.Property(e => e.AceitoEm).HasDefaultValueSql("(sysutcdatetime())", "DF_Consentimento_AceitoEm");

            entity.HasOne(d => d.Candidato).WithMany(p => p.ConsentimentoLgpds)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Consentimento_Candidato");
        });

        modelBuilder.Entity<ConteudoInstitucional>(entity =>
        {
            entity.Property(e => e.Publicado).HasDefaultValue(true, "DF_ConteudoInstitucional_Publicado");
        });

        modelBuilder.Entity<Curriculo>(entity =>
        {
            entity.Property(e => e.EnviadoEm).HasDefaultValueSql("(sysutcdatetime())", "DF_Curriculo_EnviadoEm");

            entity.HasOne(d => d.Candidato).WithMany(p => p.Curriculos)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Curriculo_Candidato");
        });

        modelBuilder.Entity<Documento>(entity =>
        {
            entity.HasOne(d => d.Conteudo).WithMany(p => p.Documentos).HasConstraintName("FK_Documento_Conteudo");
        });

        modelBuilder.Entity<LogAuditoria>(entity =>
        {
            entity.Property(e => e.DataHora).HasDefaultValueSql("(sysutcdatetime())", "DF_LogAuditoria_DataHora");

            entity.HasOne(d => d.UsuarioRh).WithMany(p => p.LogAuditoria).HasConstraintName("FK_LogAuditoria_Usuario");
        });

        modelBuilder.Entity<Modalidade>(entity =>
        {
            entity.Property(e => e.Ativo).HasDefaultValue(true, "DF_Modalidade_Ativo");
        });

        modelBuilder.Entity<Programa>(entity =>
        {
            entity.Property(e => e.Ativo).HasDefaultValue(true, "DF_Programa_Ativo");
        });

        modelBuilder.Entity<SolicitacaoTitular>(entity =>
        {
            entity.Property(e => e.AbertaEm).HasDefaultValueSql("(sysutcdatetime())", "DF_Solicitacao_AbertaEm");
            entity.Property(e => e.Status).HasDefaultValue("ABERTA", "DF_Solicitacao_Status");

            entity.HasOne(d => d.Candidato).WithMany(p => p.SolicitacaoTitulars)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Solicitacao_Candidato");
        });

        modelBuilder.Entity<TipoContrato>(entity =>
        {
            entity.Property(e => e.Ativo).HasDefaultValue(true, "DF_TipoContrato_Ativo");
        });

        modelBuilder.Entity<Turno>(entity =>
        {
            entity.Property(e => e.Ativo).HasDefaultValue(true, "DF_Turno_Ativo");
        });

        modelBuilder.Entity<Unidade>(entity =>
        {
            entity.Property(e => e.Ativo).HasDefaultValue(true, "DF_Unidade_Ativo");
            entity.Property(e => e.Uf).IsFixedLength();
        });

        modelBuilder.Entity<UsuarioRh>(entity =>
        {
            entity.HasIndex(e => e.IdentityUserId, "UX_UsuarioRh_IdentityUserId")
                .IsUnique()
                .HasFilter("([IdentityUserId] IS NOT NULL)");

            entity.Property(e => e.Ativo).HasDefaultValue(true, "DF_UsuarioRh_Ativo");
            entity.Property(e => e.CriadoEm).HasDefaultValueSql("(sysutcdatetime())", "DF_UsuarioRh_CriadoEm");

            entity.HasOne(d => d.CriadoPorUsuarioRh).WithMany(p => p.InverseCriadoPorUsuarioRh).HasConstraintName("FK_UsuarioRh_CriadoPor");

            entity.HasOne(d => d.Perfil).WithMany(p => p.UsuarioRhs)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_UsuarioRh_Perfil");
        });

        modelBuilder.Entity<Vaga>(entity =>
        {
            entity.Property(e => e.CriadaEm).HasDefaultValueSql("(sysutcdatetime())", "DF_Vaga_CriadaEm");
            entity.Property(e => e.Status).HasDefaultValue("RASCUNHO", "DF_Vaga_Status");

            entity.HasOne(d => d.Area).WithMany(p => p.Vagas)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Vaga_Area");

            entity.HasOne(d => d.Modalidade).WithMany(p => p.Vagas)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Vaga_Modalidade");

            entity.HasOne(d => d.Programa).WithMany(p => p.Vagas).HasConstraintName("FK_Vaga_Programa");

            entity.HasOne(d => d.TipoContrato).WithMany(p => p.Vagas)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Vaga_TipoContrato");

            entity.HasOne(d => d.Turno).WithMany(p => p.Vagas)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Vaga_Turno");

            entity.HasOne(d => d.Unidade).WithMany(p => p.Vagas)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Vaga_Unidade");

            entity.HasMany(d => d.Beneficios).WithMany(p => p.Vagas)
                .UsingEntity<Dictionary<string, object>>(
                    "VagaBeneficio",
                    r => r.HasOne<Beneficio>().WithMany()
                        .HasForeignKey("BeneficioId")
                        .OnDelete(DeleteBehavior.ClientSetNull)
                        .HasConstraintName("FK_VagaBeneficio_Beneficio"),
                    l => l.HasOne<Vaga>().WithMany()
                        .HasForeignKey("VagaId")
                        .HasConstraintName("FK_VagaBeneficio_Vaga"),
                    j =>
                    {
                        j.HasKey("VagaId", "BeneficioId");
                        j.ToTable("VagaBeneficio");
                    });
        });

        modelBuilder.Entity<VagaAtividade>(entity =>
        {
            entity.HasOne(d => d.Vaga).WithMany(p => p.VagaAtividades).HasConstraintName("FK_VagaAtividade_Vaga");
        });

        modelBuilder.Entity<VagaDiferencial>(entity =>
        {
            entity.HasOne(d => d.Vaga).WithMany(p => p.VagaDiferencials).HasConstraintName("FK_VagaDiferencial_Vaga");
        });

        modelBuilder.Entity<VagaRequisito>(entity =>
        {
            entity.Property(e => e.Obrigatorio).HasDefaultValue(true, "DF_VagaRequisito_Obrig");

            entity.HasOne(d => d.Vaga).WithMany(p => p.VagaRequisitos).HasConstraintName("FK_VagaRequisito_Vaga");
        });

        modelBuilder.Entity<VagaStatusHist>(entity =>
        {
            entity.Property(e => e.AlteradoEm).HasDefaultValueSql("(sysutcdatetime())", "DF_VagaStatusHist_Em");

            entity.HasOne(d => d.UsuarioRh).WithMany(p => p.VagaStatusHists).HasConstraintName("FK_VagaStatusHist_Usuario");

            entity.HasOne(d => d.Vaga).WithMany(p => p.VagaStatusHists).HasConstraintName("FK_VagaStatusHist_Vaga");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
