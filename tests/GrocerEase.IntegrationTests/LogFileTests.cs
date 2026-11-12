using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using GrocerEase.Application.Abstractions;
using GrocerEase.Application.Authentication.Commands;
using GrocerEase.Application.Authentication.Common;
using GrocerEase.Application.Common;
using GrocerEase.Contracts.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GrocerEase.IntegrationTests;

/// <summary>The log files, read from disk: one JSON document per line, a file per day, old days removed, a full file replaced.</summary>
public sealed class LogFileTests(ApiFactory factory) : IClassFixture<ApiFactory>, IDisposable
{
    private const string Password = "Sup3r-s3cret!";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"grocerease-log-test-{Guid.NewGuid():N}");

    private sealed class ThrowingHandler : ICommandHandler<LoginCommand, Result<AuthenticationResult>>
    {
        public Task<Result<AuthenticationResult>> Handle(LoginCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("the handler broke");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private WebApplicationFactory<Program> WithFiles(string? retainedDays = null, string? maxKilobytes = null, string? directory = null, string? enabled = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["LogFiles:Directory"] = directory ?? _directory };
            if (enabled is not null)
            {
                settings["LogFiles:Enabled"] = enabled;
            }

            if (retainedDays is not null)
            {
                settings["LogFiles:RetainedDays"] = retainedDays;
            }

            if (maxKilobytes is not null)
            {
                settings["LogFiles:MaxFileSizeKilobytes"] = maxKilobytes;
            }

            config.AddInMemoryCollection(settings);
        }));

    private static string[] LogFiles(string directory) =>
        [.. Directory.GetFiles(directory, "grocerease-*.log").Order(StringComparer.Ordinal)];

    /// <summary>Reads a file the app still has open for writing, so it must be opened with sharing.</summary>
    private static List<string> LinesOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static List<JsonDocument> AllEvents(string directory) =>
        [.. LogFiles(directory).SelectMany(LinesOf).Select(line => JsonDocument.Parse(line))];

    private static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, string? email = null, string password = Password) =>
        await client.PostAsJsonAsync(
            new Uri("/auth/register", UriKind.Relative),
            new RegisterRequest("Ada", "Lovelace", email ?? $"ada-{Guid.NewGuid():N}@example.com", password));

    [Fact]
    public async Task TheFile_IsNamedAfterTheDay_ItWasWritten()
    {
        var before = DateTime.Now;
        using (var app = WithFiles())
        {
            using var client = app.CreateClient();
            await RegisterAsync(client);
        }

        var after = DateTime.Now;

        var name = Path.GetFileName(Assert.Single(LogFiles(_directory)));
        Assert.Contains(name, new[] { $"grocerease-{before:yyyyMMdd}.log", $"grocerease-{after:yyyyMMdd}.log" }, StringComparer.Ordinal);
    }

    [Fact]
    public async Task TwoAppsWritingTheSameFileAtOnce_NeverCutEachOthersLinesInHalf()
    {
        // Two instances on one volume, or several hosts in one process: every line must still be one whole JSON document.
        using (var first = WithFiles())
        using (var second = WithFiles())
        {
            using var firstClient = first.CreateClient();
            using var secondClient = second.CreateClient();
            var traffic = Enumerable.Range(0, 60).Select(i => Task.Run(async () =>
            {
                var client = i % 2 == 0 ? firstClient : secondClient;
                await client.GetAsync(new Uri($"/no-such-route-{i}", UriKind.Relative));
            }));
            await Task.WhenAll(traffic);
        }

        var lines = LogFiles(_directory).SelectMany(LinesOf).ToList();
        Assert.True(lines.Count >= 60, $"expected a line for each of the 60 requests at least, found {lines.Count}");
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line); // throws on a line that was cut or interleaved
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }

        // One request event for each of the 60 requests: none lost, none doubled. (Other events of a request, such as a middleware's
        // warning, carry the path too; only request events have this template.)
        Assert.Equal(
            60,
            lines.Count(line => line.Contains("\"MessageTemplate\":\"HTTP {RequestMethod}", StringComparison.Ordinal)
                && line.Contains("/no-such-route-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheDirectory_IsCreatedWhenItDoesNotExist()
    {
        var nested = Path.Combine(_directory, "a", "b");
        Assert.False(Directory.Exists(nested));

        using (var app = WithFiles(directory: nested))
        {
            using var client = app.CreateClient();
            await RegisterAsync(client);
        }

        Assert.Single(LogFiles(nested));
    }

    [Fact]
    public async Task WithFilesTurnedOff_NoFileAndNoFolderIsCreated_AndTheOtherSinksStillReceiveEverything()
    {
        var nested = Path.Combine(_directory, "never-created");
        var sink = new CapturingLogSink();

        using (var app = WithFiles(directory: nested, enabled: "false").WithCapturedLogs(sink))
        {
            using var client = app.CreateClient();
            await RegisterAsync(client);
        }

        Assert.False(Directory.Exists(nested));
        Assert.Contains(sink.Events, e => string.Equals(CapturingLogSink.Text(e, "RequestPath"), "/auth/register", StringComparison.Ordinal)
            && string.Equals(CapturingLogSink.Text(e, "StatusCode"), "200", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task AFolderThatCanNeverBeWritten_NeverStopsTheApp_WhetherFilesAreOnOrOff(string enabled)
    {
        // The situation in a container whose user can write nowhere. A folder under a file can never be created. With files on, the
        // file sink fails quietly and drops its events; the API must keep answering either way. With files off, the path is not touched.
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "a-file");
        await File.WriteAllTextAsync(blocker, "not a folder");

        using var app = WithFiles(directory: Path.Combine(blocker, "logs"), enabled: enabled);
        using var client = app.CreateClient();
        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EveryLine_IsOneJsonDocument_WithTheTimestampTheLevelAndTheMessage()
    {
        using (var app = WithFiles())
        {
            using var client = app.CreateClient();
            await RegisterAsync(client);
            await client.GetAsync(new Uri("/no-such-route", UriKind.Relative));
        }

        var events = AllEvents(_directory);

        Assert.NotEmpty(events);
        foreach (var root in events.Select(e => e.RootElement))
        {
            Assert.True(DateTimeOffset.TryParse(root.GetProperty("Timestamp").GetString(), CultureInfo.InvariantCulture, out var timestamp));
            Assert.InRange(timestamp, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.NotEmpty(root.GetProperty("Level").GetString()!);
            Assert.NotEmpty(root.GetProperty("MessageTemplate").GetString()!);
            Assert.NotEmpty(root.GetProperty("RenderedMessage").GetString()!);
            Assert.Equal(JsonValueKind.Object, root.GetProperty("Properties").ValueKind);
        }
    }

    [Fact]
    public async Task TheRequestEvent_IsInTheFile_WithItsRequestAndResponseDetailsAsProperties()
    {
        using (var app = WithFiles())
        {
            using var client = app.CreateClient();
            await RegisterAsync(client);
        }

        var request = AllEvents(_directory).Select(e => e.RootElement)
            .Single(root => root.GetProperty("MessageTemplate").GetString()!.StartsWith("HTTP {RequestMethod}", StringComparison.Ordinal));
        var properties = request.GetProperty("Properties");

        Assert.Equal("Information", request.GetProperty("Level").GetString());
        Assert.Equal("POST", properties.GetProperty("RequestMethod").GetString());
        Assert.Equal("/auth/register", properties.GetProperty("RequestPath").GetString());
        Assert.Equal(200, properties.GetProperty("StatusCode").GetInt32());
        Assert.True(properties.GetProperty("Elapsed").GetDouble() > 0);
        Assert.Equal("GrocerEase.Api", properties.GetProperty("Application").GetString());
        Assert.StartsWith("HTTP \"POST\" \"/auth/register\" responded 200 in ", request.GetProperty("RenderedMessage").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnException_IsOneLine_WithItsFullStackTraceInsideTheJson()
    {
        using (var app = WithFiles().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ICommandHandler<LoginCommand, Result<AuthenticationResult>>, ThrowingHandler>())))
        {
            using var client = app.CreateClient();
            await client.PostAsJsonAsync(new Uri("/auth/login", UriKind.Relative), new LoginRequest("ada@example.com", Password));
        }

        var lines = LogFiles(_directory).SelectMany(LinesOf).ToList();
        var crashLine = Assert.Single(lines, line => line.Contains("Unhandled exception while handling", StringComparison.Ordinal));
        Assert.DoesNotContain('\n', crashLine); // a stack trace has many lines; the log line is one
        using var crash = JsonDocument.Parse(crashLine);
        var exception = crash.RootElement.GetProperty("Exception").GetString()!;
        Assert.Contains("System.InvalidOperationException: the handler broke", exception, StringComparison.Ordinal);
        Assert.Contains("\n   at ", exception, StringComparison.Ordinal); // the stack, with its line breaks, inside the string
        Assert.Equal("Error", crash.RootElement.GetProperty("Level").GetString());
    }

    [Fact]
    public async Task NothingTheRequestCarried_IsInTheFile()
    {
        string registeredToken;
        string registeredRefreshToken;
        using (var app = WithFiles())
        {
            using var client = app.CreateClient();
            var registered = (await (await client.PostAsJsonAsync(
                new Uri("/auth/register?secret=query-canary-31c9", UriKind.Relative),
                new RegisterRequest("Ada", "Lovelace", "file-canary@example.com", "Password-canary-8d2f!"))).Content.ReadFromJsonAsync<AuthenticationResponse>())!;
            registeredToken = registered.Token;
            registeredRefreshToken = registered.RefreshToken;
            await client.PostAsJsonAsync(new Uri("/auth/refresh", UriKind.Relative), new RefreshRequest(registered.RefreshToken));
        }

        var everything = string.Join('\n', LogFiles(_directory).SelectMany(LinesOf));

        Assert.NotEmpty(everything);
        foreach (var secret in new[] { "query-canary-31c9", "Password-canary-8d2f!", "file-canary@example.com", registeredToken, registeredRefreshToken })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FilesOlderThanTheRetention_AreDeleted_AndRecentOnesAreKept()
    {
        Directory.CreateDirectory(_directory);
        var old = Path.Combine(_directory, "grocerease-20200101.log");
        var recent = Path.Combine(_directory, $"grocerease-{DateTime.Now.AddDays(-1):yyyyMMdd}.log");
        await File.WriteAllTextAsync(old, "{}\n");
        await File.WriteAllTextAsync(recent, "{}\n");
        File.SetLastWriteTime(old, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local));
        File.SetLastWriteTime(recent, DateTime.Now.AddDays(-1));

        using (var app = WithFiles(retainedDays: "3"))
        {
            using var client = app.CreateClient();
            await RegisterAsync(client);
        }

        Assert.False(File.Exists(old), "a file from 2020 is past a 3-day retention");
        Assert.True(File.Exists(recent), "yesterday's file is within it");
        Assert.Equal(2, LogFiles(_directory).Length); // yesterday's, and today's
    }

    [Fact]
    public async Task AFileThatReachesTheSizeLimit_IsClosed_AndANumberedOneIsStarted()
    {
        const int limitKilobytes = 64;
        using (var app = WithFiles(maxKilobytes: limitKilobytes.ToString(CultureInfo.InvariantCulture)))
        {
            using var client = app.CreateClient();
            for (var i = 0; i < 150; i++)
            {
                await client.GetAsync(new Uri($"/no-such-route-{i}", UriKind.Relative));
            }
        }

        var files = LogFiles(_directory);
        Assert.True(files.Length >= 2, $"expected the day's file to roll, found {files.Length} file(s)");
        Assert.Contains(files, file => Path.GetFileName(file).Contains("_001", StringComparison.Ordinal)); // grocerease-20261110_001.log
        // The limit is checked after each write, so a file can be over it by the last event, never by more.
        const long oneEventAtMost = 16 * 1024;
        Assert.All(files, file => Assert.True(new FileInfo(file).Length <= (limitKilobytes * 1024L) + oneEventAtMost, $"{file} grew far past the limit"));
    }

    [Fact]
    public void TheSettings_AreBoundFromTheLogFilesSection_WithAFortnightAndAHundredMegabytesByDefault()
    {
        using var app = factory.WithWebHostBuilder(builder => { });

        var settings = app.Services.GetRequiredService<IOptions<GrocerEase.Api.Logging.LogFileSettings>>().Value;

        Assert.True(settings.Enabled);
        Assert.Equal(ApiFactory.TestLogDirectory, settings.Directory); // the test factory's own folder
        Assert.Equal(14, settings.RetainedDays);
        Assert.Equal(102_400, settings.MaxFileSizeKilobytes);
    }

    [Theory]
    [InlineData("0", "102400", "LogFiles:RetainedDays")]
    [InlineData("366", "102400", "LogFiles:RetainedDays")]
    [InlineData("14", "63", "LogFiles:MaxFileSizeKilobytes")]
    [InlineData("14", "1048577", "LogFiles:MaxFileSizeKilobytes")]
    public void TheAppDoesNotStart_WithARetentionOrSizeOutsideTheLimits(string retainedDays, string maxKilobytes, string expected)
    {
        using var app = WithFiles(retainedDays, maxKilobytes);

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAppDoesNotStart_WithoutALogDirectory()
    {
        using var app = WithFiles(directory: "");

        var exception = Assert.Throws<OptionsValidationException>(app.CreateClient);

        Assert.Contains("LogFiles:Directory", exception.Message, StringComparison.Ordinal);
    }
}
