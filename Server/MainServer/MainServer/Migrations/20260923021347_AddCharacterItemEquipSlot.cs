using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MainServer.Migrations
{
    /// <inheritdoc />
    public partial class AddCharacterItemEquipSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EquipSlot",
                table: "character_items",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_character_items_CharacterId_EquipSlot",
                table: "character_items",
                columns: new[] { "CharacterId", "EquipSlot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_character_items_CharacterId_EquipSlot",
                table: "character_items");

            migrationBuilder.DropColumn(
                name: "EquipSlot",
                table: "character_items");
        }
    }
}
