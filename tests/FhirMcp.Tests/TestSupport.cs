using System.Net;
using System.Text;

namespace FhirMcp.Tests;

/// <summary>Returns a canned response and remembers the last request, so tests never touch the network.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _body = body;
        _status = status;
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/fhir+json"),
        });
    }
}

/// <summary>A clock pinned to one instant, so age calculations are deterministic.</summary>
internal sealed class FixedTime : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTime(int year, int month, int day) =>
        _now = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;
}

internal static class Fixtures
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
