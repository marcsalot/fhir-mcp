using System.Net;
using System.Net.Http.Headers;
using FhirMcp.Core.Fhir;

namespace FhirMcp.Core.Auth;

/// <summary>
/// Holds the current sign-in for one FHIR server: hands out a valid access token, refreshing it
/// shortly before it expires, and reports who is signed in and which patient (if any) the
/// session is limited to.
///
/// Tokens are re-read from the store while signed out, so running <c>login</c> in a terminal
/// takes effect without restarting the MCP client.
/// </summary>
public sealed class SmartSession : IAccessScope
{
    /// <summary>Refresh this long before expiry, so a token never runs out mid-request.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly ITokenStore _store;
    private readonly SmartAuthClient _auth;
    private readonly string _fhirBaseUrl;
    private readonly string _signInHint;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SmartTokens? _tokens;

    /// <param name="signInHint">Tells the user how to sign in, such as the login command to run.</param>
    public SmartSession(ITokenStore store, SmartAuthClient auth, string fhirBaseUrl, string signInHint, TimeProvider? time = null)
    {
        _store = store;
        _auth = auth;
        _fhirBaseUrl = fhirBaseUrl;
        _signInHint = signInHint;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The current tokens, loading them from the store if needed. Null when signed out.</summary>
    public SmartTokens? Current => _tokens ??= _store.Load(_fhirBaseUrl);

    /// <summary>Who signed in, such as "Practitioner/123", for audit records.</summary>
    public string? User => Current?.FhirUser;

    public string? LockedPatientId => Current is { IsLimitedToPatient: true } t ? t.Patient : null;

    /// <summary>A valid access token, refreshed first if it is about to expire.</summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tokens = Current ?? throw NotSignedIn();
            if (tokens.ExpiresWithin(RefreshMargin, _time.GetUtcNow()))
            {
                tokens = await RefreshLockedAsync(tokens, cancellationToken);
            }
            return tokens.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Refreshes after the server rejected a token (it may have been revoked or expired early).
    /// Skips the refresh if another request already replaced the rejected token.
    /// </summary>
    public async Task<string> RefreshAfterRejectionAsync(string rejectedToken, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tokens = Current ?? throw NotSignedIn();
            if (tokens.AccessToken != rejectedToken)
            {
                return tokens.AccessToken;
            }
            return (await RefreshLockedAsync(tokens, cancellationToken)).AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SmartTokens> RefreshLockedAsync(SmartTokens tokens, CancellationToken cancellationToken)
    {
        if (tokens.RefreshToken is null)
        {
            _tokens = null;
            throw new SmartAuthException($"The sign-in to the FHIR server has expired. {_signInHint}");
        }

        try
        {
            var refreshed = await _auth.RefreshAsync(tokens, cancellationToken);
            _store.Save(refreshed);
            _tokens = refreshed;
            return refreshed;
        }
        catch (SmartAuthException ex)
        {
            // Forget the in-memory tokens so a new sign-in in the store is picked up next time.
            _tokens = null;
            throw new SmartAuthException($"The sign-in to the FHIR server could not be renewed ({ex.Message.TrimEnd('.')}). {_signInHint}", ex);
        }
    }

    private SmartAuthException NotSignedIn() =>
        new($"Not signed in to the FHIR server. {_signInHint}");
}

/// <summary>
/// Adds the session's access token to every FHIR request. If the server answers 401, the token is
/// refreshed once and the request retried; a second 401 is returned as is.
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly SmartSession _session;

    public BearerTokenHandler(SmartSession session) => _session = session;

    public BearerTokenHandler(SmartSession session, HttpMessageHandler inner) : base(inner) => _session = session;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _session.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);

        // Only bodiless requests (this client sends GETs only) can be replayed safely.
        if (response.StatusCode != HttpStatusCode.Unauthorized || request.Content is not null)
        {
            return response;
        }

        response.Dispose();
        var newToken = await _session.RefreshAfterRejectionAsync(token, cancellationToken);
        var retry = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
        {
            retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        return await base.SendAsync(retry, cancellationToken);
    }
}
