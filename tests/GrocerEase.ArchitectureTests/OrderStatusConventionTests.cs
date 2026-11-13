using System.Reflection;
using GrocerEase.Domain.Entities;

namespace GrocerEase.ArchitectureTests;

/// <summary>
/// An order's status changes only through the moves the entity offers (<c>Verify</c>, <c>Ship</c>, <c>Cancel</c>), which know what may
/// follow what. A public setter, or an <c>init</c> accessor (which is public too), would let any code put an order in any state, and the
/// state machine would be a suggestion.
/// </summary>
public class OrderStatusConventionTests
{
    private static List<string> PropertiesOfStatusTypeWithAPublicSetter(IEnumerable<Type> types) =>
        [.. types
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Where(property => property.PropertyType == typeof(OrderStatus) && property.SetMethod is { IsPublic: true })
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")];

    private sealed class Careless
    {
        public OrderStatus Status { get; set; }
    }

    [Fact]
    public void NoPropertyOfTypeOrderStatus_HasAPublicSetter()
    {
        var offenders = PropertiesOfStatusTypeWithAPublicSetter(typeof(Domain.AssemblyReference).Assembly.GetTypes());

        Assert.True(offenders.Count == 0, $"Only the moves of the entity may change a status: make the setter private on {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheRule_CanFail_AndSeesTheStatusOfAnOrder()
    {
        Assert.Equal(["Careless.Status"], PropertiesOfStatusTypeWithAPublicSetter([typeof(Careless)]), StringComparer.Ordinal);

        var status = typeof(Order).GetProperty(nameof(Order.Status));
        Assert.NotNull(status);
        Assert.Equal(typeof(OrderStatus), status.PropertyType); // the property the rule is about exists, so the rule is not passing on nothing
        Assert.False(status.SetMethod is { IsPublic: true });
    }
}
