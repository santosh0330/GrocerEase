namespace GrocerEase.UnitTests.Application;

/// <summary>A clock a test moves by hand, so "three days later" takes no waiting.</summary>
internal sealed class AdjustableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
