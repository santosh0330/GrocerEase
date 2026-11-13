using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedOrdersReadAllPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "role_claims",
                columns: new[] { "id", "role_id", "claim_type", "claim_value" },
                values: new object[] { new Guid("019e0000-0000-7000-8000-000000000118"), new Guid("019e0000-0000-7000-8000-000000000002"), "permission", "orders.read.all" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "role_claims",
                keyColumn: "id",
                keyValue: new Guid("019e0000-0000-7000-8000-000000000118"));
        }
    }
}
