using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signalko.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixLoanDateTimeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LoanedAt and ReturnedAt were created as `time` (TimeOnly) in the Init migration
            // but the entity uses DateTime?. ALTER them to datetime so inserts work correctly.
            migrationBuilder.Sql(
                "ALTER TABLE `assets_loans` MODIFY COLUMN `LoanedAt` datetime NULL;");
            migrationBuilder.Sql(
                "ALTER TABLE `assets_loans` MODIFY COLUMN `ReturnedAt` datetime NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE `assets_loans` MODIFY COLUMN `LoanedAt` time NULL;");
            migrationBuilder.Sql(
                "ALTER TABLE `assets_loans` MODIFY COLUMN `ReturnedAt` time NULL;");
        }
    }
}
