using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signalko.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCardEpcToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardEpc",
                table: "users",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "user_presence",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ScannedAt = table.Column<DateTime>(type: "datetime", nullable: false),
                    ZoneId = table.Column<int>(type: "int", nullable: true),
                    AntennaId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_presence", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_presence_antennas_AntennaId",
                        column: x => x.AntennaId,
                        principalTable: "antennas",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_user_presence_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_presence_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "zones",
                        principalColumn: "id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_user_presence_AntennaId",
                table: "user_presence",
                column: "AntennaId");

            migrationBuilder.CreateIndex(
                name: "IX_user_presence_UserId_ScannedAt",
                table: "user_presence",
                columns: new[] { "UserId", "ScannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_presence_ZoneId",
                table: "user_presence",
                column: "ZoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_presence");

            migrationBuilder.DropColumn(
                name: "CardEpc",
                table: "users");
        }
    }
}
