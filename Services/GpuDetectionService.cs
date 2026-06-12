using System.Management;
using System.Runtime.InteropServices;

namespace WinWhisperFlow.Services;

public sealed class GpuDetectionService
{
    [DllImport("nvcuda.dll", EntryPoint = "cuInit")]
    private static extern int CuInit(uint flags);

    [DllImport("nvcuda.dll", EntryPoint = "cuDeviceGetCount")]
    private static extern int CuDeviceGetCount(ref int count);

    public bool IsCudaAvailable()
    {
        try
        {
            int result = CuInit(0);
            if (result != 0) return false;
            int deviceCount = 0;
            result = CuDeviceGetCount(ref deviceCount);
            return result == 0 && deviceCount > 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public bool IsDirectMLCompatible()
    {
        Version osVersion = Environment.OSVersion.Version;
        return osVersion.Major >= 10 && osVersion.Build >= 18362;
    }

    public string GetGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_VideoController");
            using var results = searcher.Get();
            foreach (var obj in results)
            {
                string? name = obj["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }
        catch { }
        return "";
    }

    public (string provider, string gpuName) Detect()
    {
        string gpuName = GetGpuName();

        if (IsCudaAvailable())
        {
            string label = !string.IsNullOrEmpty(gpuName)
                ? $"{gpuName} (CUDA)"
                : "NVIDIA GPU (CUDA)";
            return ("cuda", label);
        }

        if (IsDirectMLCompatible())
        {
            string label = !string.IsNullOrEmpty(gpuName)
                ? $"{gpuName} (DirectML)"
                : "DirectML-compatible GPU";
            return ("dml", label);
        }

        return ("cpu", "CPU (no GPU acceleration)");
    }
}
