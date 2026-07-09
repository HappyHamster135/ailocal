namespace AiLocal.Core.Contracts;

public sealed record TokenUsage(int InputTokens, int OutputTokens)
{
    public static readonly TokenUsage Zero = new(0, 0);
}
