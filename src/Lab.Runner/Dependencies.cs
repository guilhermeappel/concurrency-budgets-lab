using System.Net;
using System.Net.Http.Json;

namespace Lab.Runner;

public interface IEnrichmentClient
{
    Task<int> GetAsync(int itemId, int part, CancellationToken cancellationToken);
}

public enum WriteOutcome
{
    Accepted,
    Rejected,
}

public interface IRecordStore
{
    Task<WriteOutcome> WriteAsync(int itemId, bool complete, CancellationToken cancellationToken);
}

public sealed record EnrichmentResponse(int ItemId, int Part, int Value);

public sealed class HttpEnrichmentClient(HttpClient http) : IEnrichmentClient
{
    public async Task<int> GetAsync(int itemId, int part, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"enrich/{itemId}/{part}", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EnrichmentResponse>(cancellationToken).ConfigureAwait(false);
        return body?.Value ?? throw new HttpRequestException("Empty enrichment response.");
    }
}

public sealed class HttpRecordStore(HttpClient http) : IRecordStore
{
    public async Task<WriteOutcome> WriteAsync(int itemId, bool complete, CancellationToken cancellationToken)
    {
        var uri = $"records/{itemId}?complete={(complete ? "true" : "false")}";
        using var response = await http.PostAsync(uri, content: null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return WriteOutcome.Rejected;
        }

        response.EnsureSuccessStatusCode();
        return WriteOutcome.Accepted;
    }
}
