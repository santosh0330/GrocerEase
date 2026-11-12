using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stores",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stores", x => x.id);
                    table.CheckConstraint("ck_stores_id_is_uuid_v7", "uuid_extract_version(id) = 7");
                });

            migrationBuilder.InsertData(
                table: "role_claims",
                columns: new[] { "id", "role_id", "claim_type", "claim_value" },
                values: new object[,]
                {
                    { new Guid("019e0000-0000-7000-8000-000000000105"), new Guid("019e0000-0000-7000-8000-000000000002"), "permission", "stores.manage" },
                    { new Guid("019e0000-0000-7000-8000-000000000106"), new Guid("019e0000-0000-7000-8000-000000000002"), "permission", "stores.read" },
                    { new Guid("019e0000-0000-7000-8000-000000000107"), new Guid("019e0000-0000-7000-8000-000000000001"), "permission", "stores.read" },
                    { new Guid("019e0000-0000-7000-8000-000000000108"), new Guid("019e0000-0000-7000-8000-000000000003"), "permission", "stores.read" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_stores_name",
                table: "stores",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stores");

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000105"));

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000106"));

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000107"));

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000108"));
        }
    }
}
