using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrocerEase.IntegrationTests;

/// <summary>A "Seq" that answers every POST with <c>500</c>, to show the API does not depend on Seq being well.</summary>
internal sealed class BrokenSeqServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private BrokenSeqServer(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    public string Url { get; }

    public static async Task<BrokenSeqServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapPost("/{**path}", () => Results.StatusCode(StatusCodes.Status500InternalServerError));
        await app.StartAsync();
        return new BrokenSeqServer(app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
