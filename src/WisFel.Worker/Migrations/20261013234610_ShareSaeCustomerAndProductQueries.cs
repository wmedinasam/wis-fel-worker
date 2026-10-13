using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WisFel.Worker.Migrations
{
    /// <inheritdoc />
    public partial class ShareSaeCustomerAndProductQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM dbo.SaeDocumentQueries AS firstQuery
                    INNER JOIN dbo.SaeDocumentQueries AS otherQuery
                        ON otherQuery.CompanyId = firstQuery.CompanyId
                        AND otherQuery.SaeVersion = firstQuery.SaeVersion
                        AND otherQuery.ReceiverSql <> firstQuery.ReceiverSql
                )
                BEGIN
                    RAISERROR(
                        'ReceiverSql differs between document types. Consolidate it per company/version before applying this migration.',
                        16,
                        1);
                    RETURN;
                END;
                """);

            migrationBuilder.CreateTable(
                name: "SaeCommonQueries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    SaeVersion = table.Column<decimal>(type: "decimal(18,1)", precision: 18, scale: 1, nullable: false),
                    CustomerSql = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductSql = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaeCommonQueries", x => x.Id);
                    table.CheckConstraint("CK_SaeCommonQueries_SaeVersion", "[SaeVersion] >= 4.0 AND [SaeVersion] * 10 = FLOOR([SaeVersion] * 10)");
                    table.ForeignKey(
                        name: "FK_SaeCommonQueries_SaeCompanies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "SaeCompanies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaeCommonQueries_CompanyId_SaeVersion",
                table: "SaeCommonQueries",
                columns: new[] { "CompanyId", "SaeVersion" },
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO dbo.SaeCommonQueries
                    (CompanyId, SaeVersion, CustomerSql, ProductSql, IsEnabled)
                SELECT query.CompanyId, query.SaeVersion, query.ReceiverSql, N'', 0
                FROM dbo.SaeDocumentQueries AS query
                WHERE query.Id =
                (
                    SELECT MIN(firstQuery.Id)
                    FROM dbo.SaeDocumentQueries AS firstQuery
                    WHERE firstQuery.CompanyId = query.CompanyId
                        AND firstQuery.SaeVersion = query.SaeVersion
                );
                """);

            migrationBuilder.DropColumn(
                name: "ReceiverSql",
                table: "SaeDocumentQueries");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaeCommonQueries");

            migrationBuilder.AddColumn<string>(
                name: "ReceiverSql",
                table: "SaeDocumentQueries",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
