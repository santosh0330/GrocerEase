using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Edited by hand. Users that exist already must end up holding a role, and the column is NOT NULL, so it is added with
            // the Customer role as a temporary default. That is the id of RoleIds.Customer, written as a literal on purpose:
            // a migration is frozen history and must not change if a constant does. The AddRoles migration seeded that role.
            var customer = new Guid("019e0000-0000-7000-8000-000000000001");

            migrationBuilder.AddColumn<Guid>(
                name: "role_id",
                table: "users",
                type: "uuid",
                nullable: false,
                defaultValue: customer);

            migrationBuilder.CreateIndex(
                name: "ix_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.AddForeignKey(
                name: "fk_users_roles_role_id",
                table: "users",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // The default only existed to fill the rows that were already there. Dropped now, so a user that is inserted
            // without a role is refused, instead of quietly becoming a customer.
            migrationBuilder.Sql("ALTER TABLE users ALTER COLUMN role_id DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_roles_role_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_role_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "users");
        }
    }
}
