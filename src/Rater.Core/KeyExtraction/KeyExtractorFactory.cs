using Rater.Core.Configuration;

namespace Rater.Core.KeyExtraction;

public class KeyExtractorFactory
{
    private readonly Dictionary<RaterKeyStrategy, IKeyExtractor> _extractors;

    public KeyExtractorFactory()
    {
        _extractors = new()
        {
            [RaterKeyStrategy.ClientId] = new ClientIdKeyExtractor(),
            [RaterKeyStrategy.IpAddress] = new IpKeyExtractor(),
            [RaterKeyStrategy.ApiKey] = new ApiKeyExtractor(),
            [RaterKeyStrategy.Composite] = new CompositeKeyExtractor(),
        };
    }


    /// <summary>
    /// Resolves the correct extractor for a given strategy.
    /// Throws clearly if an unknown strategy is configured — fail fast at startup.
    /// </summary>
    public IKeyExtractor Resolve(RaterKeyStrategy strategy)
    {
        if (_extractors.TryGetValue(strategy, out var extractor))
            return extractor;

        throw new InvalidOperationException(
            $"No key extractor registered for strategy '{strategy}'. " +
            $"Valid values: {string.Join(", ", Enum.GetNames<RaterKeyStrategy>())}");
    }
}
