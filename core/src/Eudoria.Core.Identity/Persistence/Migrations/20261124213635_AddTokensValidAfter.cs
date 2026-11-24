using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Eudoria.Core.Identity.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTokensValidAfter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "tokens_valid_after",
                schema: "identity",
                table: "accounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tokens_valid_after",
                schema: "identity",
                table: "accounts");
        }
    }
}
