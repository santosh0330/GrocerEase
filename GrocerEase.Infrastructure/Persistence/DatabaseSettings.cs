using System.ComponentModel.DataAnnotations;

namespace GrocerEase.Infrastructure.Persistence;

/// <summary>
/// The database connection. It comes from the standard <c>ConnectionStrings:Default</c> setting (the environment variable
/// <c>ConnectionStrings__Default</c>, which docker-compose.yml sets), and is checked when the app starts.
/// </summary>
public sealed class DatabaseSettings
{
    public const string ConnectionStringName = "Default";

    [Required(ErrorMessage = "ConnectionStrings:Default is required: the PostgreSQL connection string (see the README).")]
    public string ConnectionString { get; set; } = string.Empty;
}
