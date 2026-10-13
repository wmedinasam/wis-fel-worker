using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WisFel.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddSaeCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaeCompanies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SaeVersion = table.Column<decimal>(type: "decimal(18,1)", precision: 18, scale: 1, nullable: false),
                    CompanyNumber = table.Column<int>(type: "int", nullable: false),
                    IsLicensed = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LicenseUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaeCompanies", x => x.Id);
                    table.CheckConstraint("CK_SaeCompanies_CompanyNumber", "[CompanyNumber] BETWEEN 1 AND 99");
                    table.CheckConstraint("CK_SaeCompanies_LicenseTimestamp", "[IsLicensed] = 0 OR [LicenseUpdatedAt] IS NOT NULL");
                    table.CheckConstraint("CK_SaeCompanies_SaeVersion", "[SaeVersion] >= 4.0 AND [SaeVersion] * 10 = FLOOR([SaeVersion] * 10)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaeCompanies_SaeVersion_CompanyNumber",
                table: "SaeCompanies",
                columns: new[] { "SaeVersion", "CompanyNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaeCompanies");
        }
    }
}
