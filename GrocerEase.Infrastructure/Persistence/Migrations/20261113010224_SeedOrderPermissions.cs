using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedOrderPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "role_claims",
                columns: new[] { "id", "role_id", "claim_type", "claim_value" },
                values: new object[,]
                {
                    { new Guid("019e0000-0000-7000-8000-000000000114"), new Guid("019e0000-0000-7000-8000-000000000001"), "permission", "orders.place" },
                    { new Guid("019e0000-0000-7000-8000-000000000115"), new Guid("019e0000-0000-7000-8000-000000000001"), "permission", "orders.read" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000114"));

            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000115"));
        }
    }
}
