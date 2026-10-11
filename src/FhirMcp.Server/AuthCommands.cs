using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FhirMcp.Core.Auth;
using FhirMcp.Core.Fhir;

namespace FhirMcp.Server;

/// <summary>
/// The <c>login</c>, <c>logout</c> and <c>auth-status</c> commands, run in a terminal.
///
/// Sign-in happens here rather than inside the MCP server because the server runs in the
/// background under the MCP client, with no terminal and no good moment to open a browser.
/// The server only reads and renews the tokens this command saves.
/// </summary>
internal static class AuthCommands
{
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    /// <summary>What to tell the user when the server needs a sign-in.</summary>
    public static string SignInHint(string baseUrl) =>
        $"In a terminal, run: {ServerSetting(baseUrl)} \"{Environment.ProcessPath}\" login";

    /// <summary>The shortest setting that selects this server: the preset name when there is one.</summary>
    private static string ServerSetting(string baseUrl) =>
        FhirServerSettings.Presets.FirstOrDefault(p => p.BaseUrl == baseUrl) is { } preset
            ? $"{FhirServerSettings.ServerVariable}={preset.Name}"
            : $"{FhirServerSettings.BaseUrlVariable}={baseUrl}";

    public static async Task<int> LoginAsync(string[] args, string baseUrl, SmartSettings settings)
    {
        var scope = settings.Scope;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--scope" && i + 1 < args.Length)
            {
                scope = args[++i];
                SmartSettings.ValidateReadOnlyScope(scope);
            }
            else
            {
                Console.Error.WriteLine($"Unrecognized option: {args[i]}");
                Console.Error.WriteLine("Usage: login [--scope \"openid fhirUser offline_access user/*.read\"]");
                return 2;
            }
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var auth = new SmartAuthClient(http);

        try
        {
            var config = await auth.DiscoverAsync(baseUrl);
            var pkce = PkcePair.Create();
            var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

            // A loopback redirect on a free port (RFC 8252): the browser hands the code straight
            // to this process, and nothing outside this machine can receive it.
            var port = FreeLoopbackPort();
            var redirectUri = $"http://127.0.0.1:{port}/callback";
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            var authorizeUrl = SmartAuthClient.BuildAuthorizeUrl(config, settings.ClientId, redirectUri, scope, state, pkce, baseUrl);
            Console.WriteLine($"Signing in to {baseUrl}");
            Console.WriteLine("Opening your browser. If it does not open, paste this address into it:");
            Console.WriteLine();
            Console.WriteLine(authorizeUrl);
            Console.WriteLine();
            OpenBrowser(authorizeUrl);

            var code = await WaitForCallbackAsync(listener, state);
            var tokens = await auth.ExchangeCodeAsync(config, settings.ClientId, redirectUri, code, pkce, baseUrl);

            var store = settings.CreateStore();
            store.Save(tokens);

            Console.WriteLine("Signed in.");
            PrintStatus(tokens, store);
            if (!settings.Enabled)
            {
                Console.WriteLine();
                Console.WriteLine($"Note: {SmartSettings.AuthVariable} is not set to \"smart\" here. Set it in the MCP client's env for the server to use this sign-in.");
            }
            return 0;
        }
        catch (Exception ex) when (ex is SmartAuthException or HttpListenerException or TimeoutException)
        {
            Console.Error.WriteLine($"Sign-in failed: {ex.Message}");
            return 1;
        }
    }

    public static int Logout(string baseUrl, SmartSettings settings)
    {
        var store = settings.CreateStore();
        if (store.Delete(baseUrl))
        {
            Console.WriteLine($"Signed out of {baseUrl}. The saved sign-in was removed ({store.Description}).");
            Console.WriteLine("Tokens already issued stay valid until they expire; this server cannot revoke them.");
        }
        else
        {
            Console.WriteLine($"Not signed in to {baseUrl}; nothing to remove.");
        }
        return 0;
    }

    public static int Status(string baseUrl, SmartSettings settings)
    {
        Console.WriteLine($"Server: {baseUrl}");
        Console.WriteLine($"Settings: {settings.Describe()}");

        var store = settings.CreateStore();
        var tokens = store.Load(baseUrl);
        if (tokens is null)
        {
            Console.WriteLine("Not signed in.");
            return 1;
        }

        PrintStatus(tokens, store);
        return 0;
    }

    /// <summary>Who, what and until when. Never prints the tokens themselves.</summary>
    private static void PrintStatus(SmartTokens tokens, ITokenStore store)
    {
        var expires = tokens.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Console.WriteLine($"  User:          {tokens.FhirUser ?? "(not reported)"}");
        Console.WriteLine($"  Scope granted: {tokens.Scope}");
        Console.WriteLine(tokens.IsLimitedToPatient
            ? $"  Patient:       limited to {tokens.Patient}"
            : $"  Patient:       {(tokens.Patient is null ? "any the user may read" : $"{tokens.Patient} selected, but user scopes allow others")}");
        Console.WriteLine($"  Access token:  expires {expires}{(tokens.RefreshToken is null ? "; no refresh token, so sign in again then" : "; renewed automatically")}");
        Console.WriteLine($"  Saved in:      {store.Description}");
    }

    private static async Task<string> WaitForCallbackAsync(HttpListener listener, string state)
    {
        using var timeout = new CancellationTokenSource(SignInTimeout);
        while (true)
        {
            var next = listener.GetContextAsync();
            var finished = await Task.WhenAny(next, Task.Delay(Timeout.Infinite, timeout.Token).ContinueWith(_ => { }));
            if (finished != next)
            {
                throw new TimeoutException($"No sign-in response within {SignInTimeout.TotalMinutes:0} minutes.");
            }

            var context = await next;
            if (context.Request.Url?.AbsolutePath != "/callback")
            {
                context.Response.StatusCode = 404; // favicon and the like
                context.Response.Close();
                continue;
            }

            try
            {
                var code = SmartAuthClient.ReadCallback(context.Request.Url.Query, state);
                Respond(context, "Signed in to fhir-mcp. You can close this tab and return to the terminal.");
                return code;
            }
            catch (SmartAuthException ex)
            {
                Respond(context, ex.Message);
                throw;
            }
        }
    }

    private static void Respond(HttpListenerContext context, string message)
    {
        var html = $"<!doctype html><meta charset=\"utf-8\"><title>fhir-mcp</title>" +
                   $"<body style=\"font-family:system-ui;margin:3rem\"><p>{WebUtility.HtmlEncode(message)}</p></body>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes);
        context.Response.Close();
    }

    private static int FreeLoopbackPort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    private static void OpenBrowser(string url)
    {
        var opener = OperatingSystem.IsMacOS() ? "open" : OperatingSystem.IsLinux() ? "xdg-open" : null;
        try
        {
            if (opener is not null)
            {
                var info = new ProcessStartInfo(opener) { UseShellExecute = false, RedirectStandardError = true };
                info.ArgumentList.Add(url);
                Process.Start(info);
            }
            else
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // The address is printed above; opening the browser is only a convenience.
        }
    }
}
