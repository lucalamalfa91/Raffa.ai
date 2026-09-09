using System.Net;
using System.Text;

namespace Contigo.AiGateway.Tests.TestSupport;

/// <summary>
/// Fake <see cref="HttpMessageHandler"/> for every <c>Contigo.AiGateway.Tests.Foundry.*</c> test —
/// task E13/F01/US01/T02's own coding objective: "HTTP to Foundry is exercised with a fake handler
/// in tests; no live Azure in unit tests." Each call consumes the next response factory in order
/// (clamped to the last one once exhausted, for tests that only care about the first call's
/// shape); a factory, not a pre-built <see cref="HttpResponseMessage"/>, so a response used more
/// than once (for example a single-element array backing several sequential polls) still returns
/// a fresh, readable content stream every time rather than an already-consumed one.
/// </summary>
public sealed class FakeHttpMessageHandler(
    params Func<HttpRequestMessage, HttpResponseMessage>[] responseFactories) : HttpMessageHandler
{
    private int _callIndex;

    /// <summary>Every request this handler received, in call order.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Every request's raw body text (<see langword="null"/> for a request with no
    /// content), in call order — parallel to <see cref="Requests"/>. Tests assert compliance
    /// (for example "no <c>tools</c> key") directly against this raw JSON text.</summary>
    public List<string?> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(
            request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        var index = Math.Min(_callIndex, responseFactories.Length - 1);
        _callIndex++;

        return responseFactories[index](request);
    }

    /// <summary>Builds a JSON <see cref="HttpResponseMessage"/> factory — the common case for
    /// every Foundry wire response in these tests.</summary>
    public static Func<HttpRequestMessage, HttpResponseMessage> Json(
        HttpStatusCode statusCode, string json, params (string Name, string Value)[] headers)
    {
        return _ =>
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        };
    }
}
