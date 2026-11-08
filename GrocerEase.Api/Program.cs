using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services.AddScoped<ICommandHandler<RegisterCommand, AuthenticationResult>, RegisterCommandHandler>();
builder.Services.AddScoped<ICommandHandler<LoginCommand, AuthenticationResult>, LoginCommandHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();
app.MapGet("/test", () => "test");

app.Run();

// Exposes the top-level-statements entry point to WebApplicationFactory<Program> in tests.
public partial class Program;
