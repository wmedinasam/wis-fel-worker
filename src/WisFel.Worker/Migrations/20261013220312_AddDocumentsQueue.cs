using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WisFel.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentsQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    InternalNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InternalSerial = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InternalId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    UUID = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    Serial = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Observations = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.CheckConstraint("CK_Documents_Status", "[Status] IN (N'Pending', N'Processing', N'Signed', N'Delivered', N'Rejected', N'Failed')");
                    table.ForeignKey(
                        name: "FK_Documents_SaeCompanies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "SaeCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CompanyId_LocationId_DocumentType_InternalId",
                table: "Documents",
                columns: new[] { "CompanyId", "LocationId", "DocumentType", "InternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Status_Id",
                table: "Documents",
                columns: new[] { "Status", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Documents");
        }
    }
}
