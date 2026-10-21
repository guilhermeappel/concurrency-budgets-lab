using System.Net.Http.Json;

namespace Lab.Runner;

public sealed record StubStats(
    int EnrichmentCalls,
    int EnrichmentMaxInFlight,
    int EnrichmentHangs,
    int RecordsCalls,
    int RecordsMaxInFlight,
    int RecordsOfferedMax,
    int RecordsRejected);

/// <summary>Sets the scenario on the stubs and reads their counters.</summary>
public static class StubControl
{
    public static async Task PrepareAsync(HttpClient http, Scenario scenario, CancellationToken cancellationToken)
    {
        // The stubs container may still be starting; retry for up to 30 seconds.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                // Warm-up in the healthy scenario (JIT and a few connections on both
                // sides), then switch to the requested scenario and reset the counters
                // so the warm-up calls do not appear in the stats.
                using var healthyResponse = await http.PostAsync(
                    $"scenario/{LabOptions.ScenarioName(Scenario.Healthy)}", content: null, cancellationToken).ConfigureAwait(false);
                healthyResponse.EnsureSuccessStatusCode();

                for (var i = 0; i < 10; i++)
                {
                    using var warmUp = await http.GetAsync($"enrich/0/{i}", cancellationToken).ConfigureAwait(false);
                }

                using var scenarioResponse = await http.PostAsync(
                    $"scenario/{LabOptions.ScenarioName(scenario)}", content: null, cancellationToken).ConfigureAwait(false);
                scenarioResponse.EnsureSuccessStatusCode();

                using var resetResponse = await http.PostAsync("reset", content: null, cancellationToken).ConfigureAwait(false);
                resetResponse.EnsureSuccessStatusCode();
                return;
            }
            catch (HttpRequestException) when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static async Task<StubStats> GetStatsAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var stats = await http.GetFromJsonAsync<StubStats>("stats", cancellationToken).ConfigureAwait(false);
        return stats ?? throw new InvalidOperationException("The stubs returned no stats.");
    }
}
