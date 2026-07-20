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

        // Coder-tuned models beat same-size general models by a wide margin at
        // the thing this app actually does (tool-calling agents that write
        // code) - llama3.1:8b chatted politely but skipped tools; the coder
        // family follows tool schemas. All tags support Ollama tool calling.
        return budget switch
        {
            >= 24 => new("qwen2.5-coder:32b", "Qwen2.5 Coder 32B (Q4)", 24, "Stor GPU - bästa lokala kodkvaliteten"),
            >= 12 => new("qwen2.5-coder:14b", "Qwen2.5 Coder 14B (Q4)", 12, "12-16 GB VRAM - stark kodmodell"),
            >= 8  => new("qwen2.5-coder:7b",  "Qwen2.5 Coder 7B (Q4)",  6,  "8 GB VRAM - ryms helt med kontextutrymme kvar; mycket bättre på verktygsanrop och kod än llama3.1:8b"),
            >= 4  => new("llama3.2:3b", "Llama 3.2 3B (Q4)", 3, "Liten GPU"),
            _     => new("qwen2.5:1.5b", "Qwen2.5 1.5B (Q4)", 1, "CPU / small GPU - last fallback")
        };
    }
}
