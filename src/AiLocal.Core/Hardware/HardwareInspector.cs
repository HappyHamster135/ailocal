using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace AiLocal.Core.Hardware;

/// <summary>Best-effort, dependency-free hardware probe (nvidia-smi + BCL).</summary>
public static class HardwareInspector
{
    public static async Task<HardwareProfile> InspectAsync(CancellationToken ct = default)
    {
        int cores = Environment.ProcessorCount;

        // Approximate physical RAM. TotalAvailableMemoryBytes reflects the GC's
        // view of the machine/container memory limit - good enough to size a model.
        double ramGb = Math.Round(
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024d * 1024 * 1024), 1);

        string cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")
            ?? RuntimeInformation.ProcessArchitecture.ToString();

        var (gpu, gpuMem) = await TryNvidiaAsync(ct);

        return new HardwareProfile(cpu, cores, ramGb, gpu, gpuMem, CudaAvailable: gpu is not null);
    }

    private static async Task<(string? Gpu, double MemGb)> TryNvidiaAsync(CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,memory.total --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return (null, 0);

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0) return (null, 0);

            // e.g. "NVIDIA GeForce RTX 3080, 10240"
            var line = output.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (line is null) return (null, 0);

            var parts = line.Split(',');
            var name = parts[0].Trim();
            double memGb = 0;
            if (parts.Length > 1 &&
                double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var mib))
                memGb = Math.Round(mib / 1024d, 1);

            return (name, memGb);
        }
        catch
        {
            return (null, 0); // no NVIDIA GPU / driver
        }
    }
}
