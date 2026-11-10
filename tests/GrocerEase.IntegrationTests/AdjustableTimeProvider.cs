namespace GrocerEase.IntegrationTests;

/// <summary>A clock a test moves by hand, so "three days later" takes no waiting. Starts at the real time, so stored dates look real.</summary>
internal sealed class AdjustableTimeProvider : TimeProvider
{
    private long _utcTicks = DateTimeOffset.UtcNow.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
}
