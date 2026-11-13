namespace GrocerEase.Domain.Entities;

/// <summary>
/// What a customer asked one store for. It belongs to exactly one store (the store users who run it move it along) and to the customer
/// who placed it (who may read it). It is an <b>aggregate</b>: the order and its lines are saved and read as one, and an order cannot
/// exist without lines, so the only way to make one is <see cref="Place"/>.
/// </summary>
/// <remarks>
/// The total is computed from the lines, never stored: a stored total could disagree with them. A new order is
/// <see cref="OrderStatus.Pending"/>; its stock is reserved by the use case that places it, in the same unit of work.
/// </remarks>
public sealed class Order
{
    public const int RequiredIdVersion = 7;

    // One definition for the validators (a 400), the table's column and the factory.
    public const int MaxDeliveryAddressLength = 300;

    /// <summary>The most different products one order may hold. A bound keeps one request from being a bulk import.</summary>
    public const int MaxLines = 100;

    // Filled by Place, and by EF Core when it reads an order, which is why it is a field the mapping can reach and not a constructor
    // parameter (EF cannot pass a navigation to a constructor).
    private readonly List<OrderLine> _lines = [];

    // Private: EF Core binds the columns to these parameters when it reads an order. Everything else makes one with Place, which is what
    // guarantees an order has lines.
    private Order(Guid id, Guid storeId, Guid customerId, OrderStatus status, string deliveryAddress, DateTimeOffset placedAt)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(storeId, Guid.Empty); // an order is for a store
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty); // and from a customer
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Not a known order status.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryAddress);
        var trimmed = deliveryAddress.Trim(); // measured after trimming: padding is not stored, so it does not count
        if (trimmed.Length > MaxDeliveryAddressLength)
        {
            throw new ArgumentException($"The value must be at most {MaxDeliveryAddressLength} characters.", nameof(deliveryAddress));
        }

        Id = id;
        StoreId = storeId;
        CustomerId = customerId;
        Status = status;
        DeliveryAddress = trimmed;
        PlacedAt = placedAt;
    }

    public Guid Id { get; }

    public Guid StoreId { get; }

    public Guid CustomerId { get; }

    /// <summary>
    /// Where the order is in its life. Only <see cref="Verify"/>, <see cref="Ship"/> and <see cref="Cancel"/> change it (the setter is
    /// private, and an architecture test says so), so an order cannot be put in a state its history does not allow.
    /// </summary>
    public OrderStatus Status { get; private set; }

    public string DeliveryAddress { get; }

    public DateTimeOffset PlacedAt { get; }

    /// <summary>What was bought. Read only: an order's lines are fixed when it is placed.</summary>
    public IReadOnlyList<OrderLine> Lines => _lines.AsReadOnly(); // a wrapper, so a cast back to List cannot reach the field

    /// <summary>The sum of the lines, computed: it cannot disagree with them.</summary>
    public decimal Total => _lines.Sum(line => line.LineTotal);

    /// <summary>The store accepts a <see cref="OrderStatus.Pending"/> order. Returns <c>false</c> and changes nothing from any other status.</summary>
    public bool Verify() => MoveTo(OrderStatus.Verified, OrderStatus.Pending);

    /// <summary>The store sends a <see cref="OrderStatus.Verified"/> order. Returns <c>false</c> and changes nothing from any other status.</summary>
    public bool Ship() => MoveTo(OrderStatus.Shipped, OrderStatus.Verified);

    /// <summary>
    /// The order is refused or withdrawn while it has not shipped: from <see cref="OrderStatus.Pending"/> or <see cref="OrderStatus.Verified"/>.
    /// Returns <c>false</c> and changes nothing from any other status. Putting the stock back is the use case's job, in the same unit of work.
    /// </summary>
    public bool Cancel() => MoveTo(OrderStatus.Cancelled, OrderStatus.Pending, OrderStatus.Verified);

    /// <summary>
    /// The customer takes back an order the store has not looked at yet: only from <see cref="OrderStatus.Pending"/>, and it ends in the same
    /// <see cref="OrderStatus.Cancelled"/> as a cancellation by the store. Once the store has verified it, only the store can cancel.
    /// Returns <c>false</c> and changes nothing from any other status. Putting the stock back is the use case's job, in the same unit of work.
    /// </summary>
    public bool Withdraw() => MoveTo(OrderStatus.Cancelled, OrderStatus.Pending);

    // The whole state machine is these four calls: each says which statuses it may leave. A refused move is a value, not an exception,
    // because "someone already shipped it" is an expected answer to a request, not a bug.
    private bool MoveTo(OrderStatus target, params ReadOnlySpan<OrderStatus> from)
    {
        if (!from.Contains(Status))
        {
            return false;
        }

        Status = target;
        return true;
    }

    /// <summary>
    /// Makes a new <see cref="OrderStatus.Pending"/> order. The lines must belong to it (<c>OrderId</c> is this order's id), there must be
    /// at least one and no more than <see cref="MaxLines"/>, and no product may appear twice: a client that wants more of a product asks
    /// for a bigger quantity, so one line says how many.
    /// </summary>
    public static Order Place(Guid id, Guid storeId, Guid customerId, string deliveryAddress, DateTimeOffset placedAt, IEnumerable<OrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var order = new Order(id, storeId, customerId, OrderStatus.Pending, deliveryAddress, placedAt);
        foreach (var line in lines)
        {
            if (line.OrderId != id)
            {
                throw new ArgumentException($"The line for product {line.ProductId} belongs to another order.", nameof(lines));
            }

            if (order._lines.Exists(existing => existing.ProductId == line.ProductId))
            {
                throw new ArgumentException($"The product {line.ProductId} is on the order twice: ask for a bigger quantity instead.", nameof(lines));
            }

            order._lines.Add(line);
        }

        if (order._lines.Count == 0)
        {
            throw new ArgumentException("An order needs at least one line.", nameof(lines));
        }

        if (order._lines.Count > MaxLines)
        {
            throw new ArgumentException($"An order may hold at most {MaxLines} different products.", nameof(lines));
        }

        return order;
    }
}
