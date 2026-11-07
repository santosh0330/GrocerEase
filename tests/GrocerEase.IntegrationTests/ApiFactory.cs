using Microsoft.AspNetCore.Mvc.Testing;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Hosts the real API in memory. Override <c>ConfigureWebHost</c> here to replace
/// infrastructure (for example the database) once it exists.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>;
