using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carreiras.Infra.Migracoes.App
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        // Linha de base vazia: o esquema vem de db/001 e db/002. Mudanças futuras entram como migrations.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
