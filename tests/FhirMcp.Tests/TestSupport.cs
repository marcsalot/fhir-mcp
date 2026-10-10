using System.Net;
using System.Text;

namespace FhirMcp.Tests;

/// <summary>
/// Answers each request from a function of its URL and records every request,
/// so tests never touch the network and can check exactly what was sent.
/// </summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<Uri, (HttpStatusCode Status, string Body)> _respond;

    public FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        : this(_ => (status, body))
    {
    }

    public FakeHandler(Func<Uri, (HttpStatusCode Status, string Body)> respond) => _respond = respond;

    public List<Uri> Requests { get; } = new();

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        Requests.Add(request.RequestUri!);
        var (status, body) = _respond(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/fhir+json"),
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

    public const string EmptyBundle = """{ "resourceType": "Bundle", "type": "searchset", "total": 0 }""";
}
