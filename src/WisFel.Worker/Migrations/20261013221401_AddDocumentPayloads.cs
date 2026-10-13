using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WisFel.Worker.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Pdf",
                table: "Documents",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Request",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Response",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Pdf",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Request",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Response",
                table: "Documents");
        }
    }
}
