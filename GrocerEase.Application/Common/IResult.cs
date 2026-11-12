namespace GrocerEase.Application.Common;

/// <summary>
/// What every <see cref="Result{T}"/> says without its value: did the use case succeed? It lets code that wraps any handler, such
/// as the transaction decorator, act on the outcome without knowing what the handler returns.
/// </summary>
public interface IResult
{
    bool IsSuccess { get; }
}
