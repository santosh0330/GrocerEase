using FluentValidation;
using GrocerEase.Api.ExceptionHandling;
using GrocerEase.Api.Extensions;
using GrocerEase.Application;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Authentication.Handlers;
using GrocerEase.Application.Common;
using GrocerEase.Infrastructure.Authentication;
using GrocerEase.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddControllers();
builder.Services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly);
// TEMPORARY: the in-memory repository must be a singleton, or every request would see an empty store.
builder.Services.AddSingleton<IUserRepository, InMemoryUserRepository>();
builder.Services.AddSingleton<IJwtTokenGenerator, PlaceholderJwtTokenGenerator>();
builder.Services.AddValidatedCommandHandler<RegisterCommand, Result<AuthenticationResult>, RegisterCommandHandler>();
builder.Services.AddValidatedCommandHandler<LoginCommand, Result<AuthenticationResult>, LoginCommandHandler>();
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

app.MapControllers();
app.MapGet("/test", () => "test");

app.Run();

// Exposes the top-level-statements entry point to WebApplicationFactory<Program> in tests.
public partial class Program;
