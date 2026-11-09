namespace GrocerEase.Application.Common;

/// <summary>
/// The kind of an expected business failure. The Api layer decides which HTTP status each kind becomes,
/// so Application stays free of HTTP. Add a value only when a use case needs it.
/// </summary>
public enum ErrorType
{
    /// <summary>The thing asked for does not exist.</summary>
    NotFound,

    /// <summary>The request clashes with existing state, for example an email that is already registered.</summary>
    Conflict,

    /// <summary>The caller could not be authenticated, for example wrong credentials.</summary>
    Unauthorized,
}
