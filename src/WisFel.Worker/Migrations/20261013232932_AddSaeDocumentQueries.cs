using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WisFel.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddSaeDocumentQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SaeDocumentQueries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    SaeVersion = table.Column<decimal>(type: "decimal(18,1)", precision: 18, scale: 1, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DocumentSql = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssuerSql = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiverSql = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ItemsSql = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FreeFieldsSql = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaeDocumentQueries", x => x.Id);
                    table.CheckConstraint("CK_SaeDocumentQueries_DocumentType", "LEN([DocumentType]) > 0 AND [DocumentType] = UPPER([DocumentType])");
                    table.CheckConstraint("CK_SaeDocumentQueries_SaeVersion", "[SaeVersion] >= 4.0 AND [SaeVersion] * 10 = FLOOR([SaeVersion] * 10)");
                    table.ForeignKey(
                        name: "FK_SaeDocumentQueries_SaeCompanies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "SaeCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaeDocumentQueries_CompanyId_SaeVersion_DocumentType",
                table: "SaeDocumentQueries",
                columns: new[] { "CompanyId", "SaeVersion", "DocumentType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaeDocumentQueries");
        }
    }
}
