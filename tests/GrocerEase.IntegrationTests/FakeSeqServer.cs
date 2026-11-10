using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GrocerEase.IntegrationTests;

/// <summary>
/// Stands in for a Seq server: accepts what the Seq sink posts, answers like Seq does (<c>201</c>), and keeps what it was sent.
/// It is a real HTTP server on a free local port, so the real sink, with its real batching and HTTP client, is what is tested.
/// </summary>
internal sealed class FakeSeqServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeSeqServer(WebApplication app, string url, ConcurrentQueue<(string Path, string? ApiKey, string Body)> posts)
    {
        _app = app;
        Url = url;
        Posts = posts;
    }

    public string Url { get; }

    /// <summary>One entry per POST: the path, the API key header (if any), and the body.</summary>
    public ConcurrentQueue<(string Path, string? ApiKey, string Body)> Posts { get; }

    /// <summary>Every event received, one JSON document per line of every body (the format Seq ingests is newline-delimited JSON).</summary>
    public List<JsonElement> Events =>
        [.. Posts.SelectMany(post => post.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];

    public static async Task<FakeSeqServer> StartAsync()
    {
        var posts = new ConcurrentQueue<(string Path, string? ApiKey, string Body)>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();

        // Routes must exist before the server starts: one added afterwards is never served.
        app.MapPost("/{**path}", async (HttpContext context, string path) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            posts.Enqueue(($"/{path}", context.Request.Headers["X-Seq-ApiKey"].FirstOrDefault(), await reader.ReadToEndAsync()));
            return Results.Text("{}", "application/json", statusCode: StatusCodes.Status201Created);
        });

        await app.StartAsync();
        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new FakeSeqServer(app, url, posts);
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}
