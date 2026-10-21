using Lab.Runner;

namespace Lab.Tests;

public sealed class LabOptionsTests
{
    [Fact]
    public void Defaults_are_valid()
    {
        Assert.Empty(new LabOptions().Validate());
    }

    [Theory]
    [InlineData("--records-limit", "0")]
    [InlineData("--enrichment-limit", "-1")]
    [InlineData("--item-parallelism", "0")]
    public void Non_positive_limits_are_rejected_before_any_request(string key, string value)
    {
        var options = LabOptions.Parse([key, value]);

        Assert.NotEmpty(options.Validate());
    }

    [Fact]
    public void An_enrichment_budget_the_item_loop_can_never_reach_is_rejected()
    {
        var options = new LabOptions { Strategy = StrategyKind.C4, ItemParallelism = 8, EnrichmentLimit = 24 };

        Assert.Contains(options.Validate(), e => e.Contains("--enrichment-limit"));
    }

    [Fact]
    public void Parse_reads_strategy_and_scenario()
    {
        var options = LabOptions.Parse(["--strategy", "c3", "--scenario", "records-degraded", "--stubs", "http://stubs:5080"]);

        Assert.Equal(StrategyKind.C3, options.Strategy);
        Assert.Equal(Scenario.RecordsDegraded, options.Scenario);
        Assert.Equal(new Uri("http://stubs:5080/"), options.StubsBaseAddress);
    }
}
