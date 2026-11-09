using GrocerEase.Api.Authentication;
using GrocerEase.Api.ExceptionHandling;
using GrocerEase.Api.Health;
using GrocerEase.Api.RateLimiting;
using GrocerEase.Application;
using GrocerEase.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);
builder.Services.AddRateLimiting(builder.Configuration);
builder.Services.AddJwtAuthentication();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<UnhandledExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/test", () => "test");
app.MapHealthEndpoints();

app.Run();

// Exposes the top-level-statements entry point to WebApplicationFactory<Program> in tests.
public partial class Program;
