namespace AiLocal.Core.Hardware;

/// <summary>A snapshot of a node's compute capacity, used to recommend a local model.</summary>
public sealed record HardwareProfile(
    string Cpu,
    int LogicalCores,
    double SystemMemoryGb,
    string? Gpu,
    double GpuMemoryGb,
    bool CudaAvailable);
