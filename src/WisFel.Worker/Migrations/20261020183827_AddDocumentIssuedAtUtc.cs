using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WisFel.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentIssuedAtUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IssuedAtUtc",
                table: "Documents",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IssuedAtUtc",
                table: "Documents");
        }
    }
}
