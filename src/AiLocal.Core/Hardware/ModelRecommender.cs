namespace AiLocal.Core.Hardware;

public sealed record LocalModelRecommendation(
    string OllamaTag,
    string DisplayName,
    double MinBudgetGb,
    string Notes);

/// <summary>Maps a hardware profile to a local Ollama model that should fit.</summary>
public static class ModelRecommender
{
    public static LocalModelRecommendation Recommend(HardwareProfile hw)
    {
        // Prefer VRAM; fall back to half of system RAM (capped) for CPU inference.
        double budget = hw.CudaAvailable && hw.GpuMemoryGb > 0
            ? hw.GpuMemoryGb
            : Math.Min(hw.SystemMemoryGb * 0.5, 8);

        return budget switch
        {
            >= 24 => new("qwen2.5:32b", "Qwen2.5 32B (Q4)", 24, "Large GPU - high quality"),
            >= 14 => new("qwen2.5:14b", "Qwen2.5 14B (Q4)", 12, "Bra balans kvalitet/VRAM"),
            >= 8  => new("llama3.1:8b", "Llama 3.1 8B (Q4)", 6,  "Fits 8-12 GB VRAM, such as RTX 3080"),
            >= 4  => new("llama3.2:3b", "Llama 3.2 3B (Q4)", 3,  "Liten GPU"),
            _     => new("qwen2.5:1.5b","Qwen2.5 1.5B (Q4)", 1,  "CPU / small GPU - last fallback")
        };
    }
}
