namespace GrocerEase.Domain.Entities;

/// <summary>
/// The fact that a user works for a store: "this person is one of the people who run that store". It is what tells the system which
/// store a store user may manage, because the <c>StoreUser</c> role only says what kind of user they are. A user is a member of at
/// most one store (the table's unique index on the user says so). Like the other entities, it cannot exist with an empty id.
/// </summary>
public sealed class StoreMember
{
    public const int RequiredIdVersion = 7;

    public StoreMember(Guid id, Guid storeId, Guid userId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (id.Version != RequiredIdVersion)
        {
            throw new ArgumentException($"The id must be a version {RequiredIdVersion} UUID (use Guid.CreateVersion7()).", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(storeId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);

        Id = id;
        StoreId = storeId;
        UserId = userId;
    }

    public Guid Id { get; }

    public Guid StoreId { get; }

    public Guid UserId { get; }
}
