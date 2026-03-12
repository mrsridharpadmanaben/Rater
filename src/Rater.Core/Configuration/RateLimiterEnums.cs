using System.Text.Json.Serialization;

namespace Rater.Core.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RaterAlgorithm
{
    FixedWindow = 0,
    TokenBucket,
    SlidingWindowCounter
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RaterKeyStrategy
{
    IpAddress = 0,
    ClientId,
    ApiKey,
    Composite
}


[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RaterStorage
{
    InMemory = 0,
    Redis
}