namespace Rater.Core.Contracts;

public class StatusResponse
{
    public string ClientKey { get; set; } = string.Empty;
    public List<RuleStatus> ActiveRules { get; set; } = new();
}

public class RuleStatus
{
    public string RuleName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public int Limit { get; set; }
    public int CurrentCount { get; set; }
    public int Remaining { get; set; }
    public DateTimeOffset ResetAt { get; set; }
}