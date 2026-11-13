using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GrocerEase.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_actor_id_matches_reason", "(reason = 'OpeningBalance') = (actor_id IS NULL)");
                    table.CheckConstraint("ck_stock_movements_delta_in_range", "delta <> 0 AND delta >= -1000000000 AND delta <= 1000000000");
                    table.CheckConstraint("ck_stock_movements_delta_matches_reason", "(reason = 'Correction') OR (reason = 'OrderPlaced' AND delta < 0) OR (reason IN ('OpeningBalance', 'Received', 'OrderCancelled') AND delta > 0)");
                    table.CheckConstraint("ck_stock_movements_id_is_uuid_v7", "uuid_extract_version(id) = 7");
                    table.CheckConstraint("ck_stock_movements_order_id_matches_reason", "(reason IN ('OrderPlaced', 'OrderCancelled')) = (order_id IS NOT NULL)");
                    table.CheckConstraint("ck_stock_movements_reason_is_known", "reason IN ('OpeningBalance', 'Received', 'Correction', 'OrderPlaced', 'OrderCancelled')");
                    table.ForeignKey(
                        name: "fk_stock_movements_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_products_product_id_store_id",
                        columns: x => new { x.product_id, x.store_id },
                        principalTable: "products",
                        principalColumns: new[] { "id", "store_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_stock_movements_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_actor_id",
                table: "stock_movements",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_order_id",
                table: "stock_movements",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_product_id_created_at",
                table: "stock_movements",
                columns: new[] { "product_id", "created_at", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_product_id_store_id",
                table: "stock_movements",
                columns: new[] { "product_id", "store_id" });

            // An order's stock is taken before the order row is written, in the same transaction, so the order's foreign key is checked when the
            // transaction commits, not when the movement is written. Entity Framework cannot say DEFERRABLE, so it is said here.
            migrationBuilder.Sql(
                "ALTER TABLE stock_movements ALTER CONSTRAINT fk_stock_movements_orders_order_id DEFERRABLE INITIALLY DEFERRED;");

            // The ledger is append-only: the application's database role may add a row and read rows, and nothing else, so no bug in the
            // application can rewrite history. (The role is made by docker/postgres/init/01-roles.sh and given every privilege on every table
            // the migrator creates; this takes two back for this table. A database without that role has nothing to take back.)
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'grocerease_app') THEN
                        REVOKE UPDATE, DELETE ON stock_movements FROM grocerease_app;
                    END IF;
                END
                $$;
                """);

            // The ledger must add up from day one: every product that already has stock gets one movement for what it has. A row at zero has
            // nothing to explain (a movement of nothing is refused). There is no actor and no order: nobody did it.
            migrationBuilder.Sql(
                """
                INSERT INTO stock_movements (id, product_id, store_id, delta, reason, actor_id, order_id, created_at)
                SELECT uuidv7(), product_id, store_id, quantity_on_hand, 'OpeningBalance', NULL, NULL, now()
                FROM inventory_items
                WHERE quantity_on_hand > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_movements");
        }
    }
}
