using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_id_is_uuid_v7", "uuid_extract_version(id) = 7");
                    table.CheckConstraint("ck_products_price_is_positive", "price > 0");
                    table.ForeignKey(
                        name: "fk_products_stores_store_id",
                        column: x => x.store_id,
                        principalTable: "stores",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "role_claims",
                columns: new[] { "id", "role_id", "claim_type", "claim_value" },
                values: new object[,]
                {
                    { new Guid("019e0000-0000-7000-8000-000000000109"), new Guid("019e0000-0000-7000-8000-000000000002"), "permission", "products.read" },
                    { new Guid("019e0000-0000-7000-8000-000000000110"), new Guid("019e0000-0000-7000-8000-000000000001"), "permission", "products.read" },
                    { new Guid("019e0000-0000-7000-8000-000000000111"), new Guid("019e0000-0000-7000-8000-000000000003"), "permission", "products.read" },
                    { new Guid("019e0000-0000-7000-8000-000000000112"), new Guid("019e0000-0000-7000-8000-000000000003"), "permission", "products.manage" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_store_id_name",
                table: "products",
                columns: new[] { "store_id", "name" },
                unique: true,
                filter: "archived_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000109"));

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000110"));

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000111"));

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000112"));
        }
    }
}
