using GrocerEase.Application.Common;
using GrocerEase.Application.Orders.Commands;
using GrocerEase.Application.Orders.Common;
using GrocerEase.Application.Orders.Handlers;
using GrocerEase.Application.Products.Common;
using GrocerEase.Application.Stores.Common;
using GrocerEase.Domain.Entities;

namespace GrocerEase.UnitTests.Application;

public class PlaceOrderCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeStoreRepository _stores = new();
    private readonly FakeProductRepository _products = new();
    private readonly FakeInventoryItemRepository _stock = new();
    private readonly FakeOrderRepository _orders = new();
    private readonly Guid _customerId = Guid.CreateVersion7();
    private readonly Store _store = new(Guid.CreateVersion7(), "Fresh Mart", "1 High Street");

    public PlaceOrderCommandHandlerTests() =>
        Assert.True(_stores.TryAddAsync(_store, CancellationToken.None).Result);

    private PlaceOrderCommandHandler Handler() => new(_stores, _products, _stock, _orders, new AdjustableTimeProvider(Now));

    private Product AddProduct(string name, decimal price, int? stock, Guid? storeId = null, bool archived = false)
    {
        var product = new Product(Guid.CreateVersion7(), storeId ?? _store.Id, name, "", price, archived ? Now : null);
        _products.Products.Add(product);
        if (stock is { } quantity)
        {
            _stock.Quantities[(product.StoreId, product.Id)] = quantity;
        }

        return product;
    }

    private PlaceOrderCommand Order(params (Product Product, int Quantity)[] lines) =>
        new(_store.Id, _customerId, "1 High Street", [.. lines.Select(line => new OrderLineInput(line.Product.Id, line.Quantity))]);

    [Fact]
    public async Task APlacedOrder_IsPending_CopiesTheNamesAndPrices_ReservesTheStock_AndIsStored()
    {
        var milk = AddProduct("Milk", 2.50m, stock: 10);
        var bread = AddProduct("Bread", 1.10m, stock: 5);

        var result = await Handler().Handle(Order((milk, 2), (bread, 1)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var order = result.Value;
        Assert.Equal(("Pending", _store.Id, "1 High Street", Now), (order.Status, order.StoreId, order.DeliveryAddress, order.PlacedAt));
        Assert.Equal(6.10m, order.Total); // 2 x 2.50 + 1 x 1.10
        Assert.Equal(
            [("Bread", 1.10m, 1, 1.10m), ("Milk", 2.50m, 2, 5.00m)],
            order.Lines.Select(line => (line.ProductName, line.UnitPrice, line.Quantity, line.LineTotal)));
        Assert.Equal((8, 4), (_stock.Quantities[(_store.Id, milk.Id)], _stock.Quantities[(_store.Id, bread.Id)]));
        var stored = Assert.Single(_orders.Orders);
        Assert.Equal((order.Id, _customerId), (stored.Id, stored.CustomerId));
    }

    [Fact]
    public async Task TheCustomer_IsTheOneInTheCommand_AndTheOrderIsTheirs()
    {
        var milk = AddProduct("Milk", 2.50m, stock: 10);

        var result = await Handler().Handle(Order((milk, 1)), CancellationToken.None);

        Assert.Equal(_customerId, Assert.Single(_orders.Orders).CustomerId);
        Assert.NotNull(await _orders.GetForCustomerAsync(_customerId, result.Value.Id, CancellationToken.None));
        Assert.Null(await _orders.GetForCustomerAsync(Guid.CreateVersion7(), result.Value.Id, CancellationToken.None));
    }

    [Fact]
    public async Task WhenALineCannotBeFilled_TheAnswerNamesTheProduct_AndNoOrderIsStored()
    {
        var milk = AddProduct("Milk", 2.50m, stock: 1);

        var result = await Handler().Handle(Order((milk, 2)), CancellationToken.None);

        Assert.Equal("Order.InsufficientStock", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Contains("Milk", result.Error.Description, StringComparison.Ordinal);
        Assert.Empty(_orders.Orders);
        Assert.Equal(1, _stock.Quantities[(_store.Id, milk.Id)]);
    }

    [Fact]
    public async Task AProductThatHasNeverHadStock_CannotBeOrdered()
    {
        var milk = AddProduct("Milk", 2.50m, stock: null);

        var result = await Handler().Handle(Order((milk, 1)), CancellationToken.None);

        Assert.Equal("Order.InsufficientStock", result.Error.Code);
        Assert.Empty(_orders.Orders);
    }

    [Fact]
    public async Task TheLines_AreReservedInOrderOfProductId_WhateverOrderTheyWereAskedFor_SoTwoOrdersCannotDeadlock()
    {
        var products = Enumerable.Range(0, 5).Select(i => AddProduct($"P{i}", 1m, stock: 10)).ToList();
        var shuffled = products.OrderByDescending(product => product.Id).ToList(); // asked for in the opposite order

        await Handler().Handle(Order([.. shuffled.Select(product => (product, 1))]), CancellationToken.None);

        Assert.Equal(products.OrderBy(product => product.Id).Select(product => product.Id), _stock.Calls.Select(call => call.ProductId));
        Assert.All(_stock.Calls, call => Assert.Equal(-1, call.Delta)); // a reservation removes stock
    }

    [Fact]
    public async Task AnUnknownProduct_AnArchivedOne_AndAnotherStoresOne_AreNotAvailable_AndNoStockIsTouched()
    {
        var milk = AddProduct("Milk", 2.50m, stock: 10);
        var archived = AddProduct("Old", 1m, stock: 10, archived: true);
        var foreign = AddProduct("Foreign", 1m, stock: 10, storeId: Guid.CreateVersion7());
        var unknown = new Product(Guid.CreateVersion7(), _store.Id, "Unknown", "", 1m);

        foreach (var other in new[] { archived, foreign, unknown })
        {
            var result = await Handler().Handle(Order((milk, 1), (other, 1)), CancellationToken.None);

            Assert.Same(ProductErrors.NotFound, result.Error);
        }

        Assert.Empty(_stock.Calls); // not even the good line was reserved: the products are checked before any stock is taken
        Assert.Empty(_orders.Orders);
    }

    [Fact]
    public async Task AnUnknownStore_IsNotFound_AndNothingHappens()
    {
        var milk = AddProduct("Milk", 2.50m, stock: 10);

        var result = await Handler().Handle(Order((milk, 1)) with { StoreId = Guid.CreateVersion7() }, CancellationToken.None);

        Assert.Same(StoreErrors.NotFound, result.Error);
        Assert.Empty(_stock.Calls);
    }

    [Fact]
    public async Task TheDeliveryAddress_IsTrimmed()
    {
        var milk = AddProduct("Milk", 2.50m, stock: 10);

        var result = await Handler().Handle(Order((milk, 1)) with { DeliveryAddress = "  1 High Street  " }, CancellationToken.None);

        Assert.Equal("1 High Street", result.Value.DeliveryAddress);
    }

    [Fact]
    public async Task GetMyOrder_ReturnsTheCustomersOwnOrder_AndNotFoundForSomeoneElsesOrAnUnknownOne()
    {
        var milk = AddProduct("Milk", 2.50m, stock: 10);
        var placed = (await Handler().Handle(Order((milk, 1)), CancellationToken.None)).Value;
        var get = new GetMyOrderQueryHandler(_orders);

        var mine = await get.Handle(new GrocerEase.Application.Orders.Queries.GetMyOrderQuery(_customerId, placed.Id), CancellationToken.None);
        var theirs = await get.Handle(new GrocerEase.Application.Orders.Queries.GetMyOrderQuery(Guid.CreateVersion7(), placed.Id), CancellationToken.None);
        var unknown = await get.Handle(new GrocerEase.Application.Orders.Queries.GetMyOrderQuery(_customerId, Guid.CreateVersion7()), CancellationToken.None);

        Assert.Equal((placed.Id, placed.Total, placed.Status), (mine.Value.Id, mine.Value.Total, mine.Value.Status));
        Assert.Equal(placed.Lines, mine.Value.Lines); // the same lines, as the stored order has them
        Assert.Same(OrderErrors.NotFound, theirs.Error); // the same error as an order that does not exist
        Assert.Same(OrderErrors.NotFound, unknown.Error);
    }
}
