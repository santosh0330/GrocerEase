using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexOrdersByStoreAndTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_store_id",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ix_orders_store_id_placed_at",
                table: "orders",
                columns: new[] { "store_id", "placed_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_store_id_placed_at",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "ix_orders_store_id",
                table: "orders",
                column: "store_id");
        }
    }
}
