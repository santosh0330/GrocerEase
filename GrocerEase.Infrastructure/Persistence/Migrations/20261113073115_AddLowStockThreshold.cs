using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLowStockThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "low_stock_threshold",
                table: "inventory_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_items_low_stock_threshold_in_range",
                table: "inventory_items",
                sql: "low_stock_threshold IS NULL OR (low_stock_threshold >= 0 AND low_stock_threshold <= 1000000000)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_items_low_stock_threshold_in_range",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "low_stock_threshold",
                table: "inventory_items");
        }
    }
}
