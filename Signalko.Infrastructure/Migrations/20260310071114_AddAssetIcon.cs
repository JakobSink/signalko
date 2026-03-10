using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signalko.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only add the Icon column — other schema changes already exist in the live DB
            migrationBuilder.Sql(
                "ALTER TABLE `ASSET` ADD COLUMN IF NOT EXISTS `Icon` varchar(512) CHARACTER SET utf8mb4 NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Icon",
                table: "ASSET");
        }
    }
}
