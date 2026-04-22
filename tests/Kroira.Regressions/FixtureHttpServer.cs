using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Kroira.Regressions;

internal sealed class FixtureHttpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private readonly string _caseDirectory;
    private readonly FixtureServerManifest _manifest;

    private FixtureHttpServer(string caseDirectory, string baseUrl, FixtureServerManifest manifest)
    {
        _caseDirectory = caseDirectory;
        BaseUrl = baseUrl.TrimEnd('/');
        _manifest = manifest;
        _listener.Prefixes.Add($"{BaseUrl}/");
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    public string BaseUrl { get; }

    public static async Task<FixtureHttpServer> StartAsync(string caseDirectory)
    {
        var port = ReserveTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}";
        var manifestPath = Path.Combine(caseDirectory, "server.json");
        var manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<FixtureServerManifest>(
                  await File.ReadAllTextAsync(manifestPath),
                  RegressionJson.Options) ?? new FixtureServerManifest()
            : new FixtureServerManifest();

        return new FixtureHttpServer(caseDirectory, baseUrl, manifest);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
        }

        try
        {
            await _acceptLoop;
        }
        catch
        {
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var requestPath = context.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;
            var route = MatchRoute(requestPath, context.Request.QueryString);
            if (route != null)
            {
                if (route.DelayMs > 0)
                {
                    await Task.Delay(route.DelayMs, cancellationToken);
                }

                var body = await ResolveRouteBodyAsync(route);
                await WriteResponseAsync(context.Response, route.StatusCode, ResolveContentType(route.ContentType, route.BodyFile, requestPath), body);
                return;
            }

            var localPath = Path.Combine(_caseDirectory, requestPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath))
            {
                var body = Tokenize(await File.ReadAllTextAsync(localPath, cancellationToken));
                await WriteResponseAsync(context.Response, 200, ResolveContentType(string.Empty, localPath, requestPath), body);
                return;
            }

            await WriteResponseAsync(context.Response, 404, "text/plain; charset=utf-8", "Not found");
        }
        catch (OperationCanceledException)
        {
            try
            {
                context.Response.Abort();
            }
            catch
            {
            }
        }
        catch (Exception ex)
        {
            await WriteResponseAsync(context.Response, 500, "text/plain; charset=utf-8", ex.Message);
        }
    }

    private FixtureServerRouteDefinition? MatchRoute(string requestPath, System.Collections.Specialized.NameValueCollection query)
    {
        foreach (var route in _manifest.Routes)
        {
            if (!string.Equals(NormalizePath(route.Path), NormalizePath(requestPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (route.Query.Count == 0)
            {
                return route;
            }

            var matched = route.Query.All(pair =>
                string.Equals(query[pair.Key], pair.Value, StringComparison.OrdinalIgnoreCase));
            if (matched)
            {
                return route;
            }
        }

        return null;
    }

    private async Task<string> ResolveRouteBodyAsync(FixtureServerRouteDefinition route)
    {
        if (!string.IsNullOrWhiteSpace(route.BodyFile))
        {
            var path = Path.Combine(_caseDirectory, route.BodyFile.Replace('/', Path.DirectorySeparatorChar));
            return Tokenize(await File.ReadAllTextAsync(path));
        }

        return Tokenize(route.Body);
    }

    private string Tokenize(string value)
    {
        return value.Replace("{{server}}", BaseUrl, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, int statusCode, string contentType, string body)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = payload.Length;
        await using var stream = response.OutputStream;
        await stream.WriteAsync(payload);
    }

    private static string NormalizePath(string path)
    {
        return (path ?? string.Empty).Trim().TrimStart('/');
    }

    private static int ReserveTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string ResolveContentType(string explicitContentType, string bodyFile, string requestPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitContentType))
        {
            return explicitContentType;
        }

        var extension = Path.GetExtension(!string.IsNullOrWhiteSpace(bodyFile) ? bodyFile : requestPath).ToLowerInvariant();
        return extension switch
        {
            ".json" => "application/json; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".m3u" or ".m3u8" => "application/vnd.apple.mpegurl; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "text/plain; charset=utf-8"
        };
    }
}
