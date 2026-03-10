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
            migrationBuilder.AddColumn<string>(
                name: "Icon",
                table: "ASSET",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true);
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
