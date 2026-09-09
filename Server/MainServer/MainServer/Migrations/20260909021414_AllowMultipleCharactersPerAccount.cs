using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainServer.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleCharactersPerAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL/MariaDB는 외래키가 의존 중인 인덱스를 바로 드롭할 수 없으므로,
            // FK를 먼저 내린 뒤 인덱스를 비유니크로 재생성하고 FK를 다시 건다.
            migrationBuilder.DropForeignKey(
                name: "FK_characters_users_UserId",
                table: "characters");

            migrationBuilder.DropIndex(
                name: "IX_characters_UserId",
                table: "characters");

            migrationBuilder.CreateIndex(
                name: "IX_characters_UserId",
                table: "characters",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_characters_users_UserId",
                table: "characters",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_characters_users_UserId",
                table: "characters");

            migrationBuilder.DropIndex(
                name: "IX_characters_UserId",
                table: "characters");

            migrationBuilder.CreateIndex(
                name: "IX_characters_UserId",
                table: "characters",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_characters_users_UserId",
                table: "characters",
                column: "UserId",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
