using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "uq_products_id_store_id",
                table: "products",
                columns: new[] { "id", "store_id" });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity_on_hand = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => x.product_id);
                    table.CheckConstraint("ck_inventory_items_quantity_in_range", "quantity_on_hand >= 0 AND quantity_on_hand <= 1000000000");
                    table.ForeignKey(
                        name: "fk_inventory_items_products_product_id_store_id",
                        columns: x => new { x.product_id, x.store_id },
                        principalTable: "products",
                        principalColumns: new[] { "id", "store_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "role_claims",
                columns: new[] { "id", "role_id", "claim_type", "claim_value" },
                values: new object[] { new Guid("019e0000-0000-7000-8000-000000000113"), new Guid("019e0000-0000-7000-8000-000000000003"), "permission", "inventory.manage" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_product_id_store_id",
                table: "inventory_items",
                columns: new[] { "product_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_store_id",
                table: "inventory_items",
                column: "store_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropUniqueConstraint(
                name: "uq_products_id_store_id",
                table: "products");

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000113"));
        }
    }
}
