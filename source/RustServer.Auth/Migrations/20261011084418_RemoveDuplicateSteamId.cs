using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustServer.Auth.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDuplicateSteamId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SteamId",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SteamId",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);
        }
    }
}
