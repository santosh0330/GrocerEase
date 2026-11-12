using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    claim_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.CheckConstraint("ck_role_claims_id_is_uuid_v7", "uuid_extract_version(id) = 7");
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "role_claims",
                columns: new[] { "id", "role_id", "claim_type", "claim_value" },
                values: new object[,]
                {
                    { new Guid("019e0000-0000-7000-8000-000000000101"), new Guid("019e0000-0000-7000-8000-000000000001"), "permission", "profile.read" },
                    { new Guid("019e0000-0000-7000-8000-000000000102"), new Guid("019e0000-0000-7000-8000-000000000002"), "permission", "profile.read" },
                    { new Guid("019e0000-0000-7000-8000-000000000103"), new Guid("019e0000-0000-7000-8000-000000000002"), "permission", "roles.read" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id_claim_type_claim_value",
                table: "role_claims",
                columns: new[] { "role_id", "claim_type", "claim_value" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_claims");
        }
    }
}
