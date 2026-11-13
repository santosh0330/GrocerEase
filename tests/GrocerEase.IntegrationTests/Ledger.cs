namespace GrocerEase.IntegrationTests;

/// <summary>
/// The invariant the stock ledger exists for: <b>the sum of a product's movements is its quantity</b>. If the two ever differ, some writer
/// changed the quantity without the ledger, or the other way round. Any test that moves stock can end by asserting it.
/// </summary>
internal static class Ledger
{
    /// <summary>The products of the store whose movements do not add up to their quantity, as readable lines (empty when the ledger agrees).</summary>
    public static async Task<IReadOnlyList<string>> DisagreementsAsync(Guid storeId)
    {
        var connection = await PostgresServer.ApplicationConnectionStringAsync();
        await using var db = new Npgsql.NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """
            select p.name || ' (' || p.id || '): quantity ' || coalesce(i.quantity_on_hand, 0) || ', movements add up to ' || coalesce(s.total, 0)
            from products p
            left join inventory_items i on i.product_id = p.id
            left join (select product_id, sum(delta) as total from stock_movements group by product_id) s on s.product_id = p.id
            where p.store_id = @store and coalesce(i.quantity_on_hand, 0) <> coalesce(s.total, 0)
            order by p.name
            """,
            db);
        command.Parameters.AddWithValue("store", storeId);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        return lines;
    }

    /// <summary>Fails, naming the products, when the ledger of the store does not add up to its stock.</summary>
    public static async Task AgreesAsync(Guid storeId)
    {
        var disagreements = await DisagreementsAsync(storeId);

        Assert.True(disagreements.Count == 0, $"The ledger does not add up to the stock: {string.Join("; ", disagreements)}");
    }

    /// <summary>How many movements the store's products have, of any reason (for tests that count them).</summary>
    public static async Task<long> CountAsync(Guid storeId) =>
        await PostgresServer.ScalarAsync<long>(await PostgresServer.ApplicationConnectionStringAsync(), $"select count(*) from stock_movements where store_id = '{storeId}'");
}
