using System.Text.Json;
using ZenStates.Core;

internal record Baseline(uint FMax, int[] Curve);

internal static class Program
{
    static readonly string BaselinePath = Path.Combine(AppContext.BaseDirectory, "cpu-baseline.json");

    static uint CoreMask(int coreIndex)
    {
        int ccdIndex = coreIndex / 8;
        int localCoreIndex = coreIndex % 8;
        return (uint)(((ccdIndex << 8) | localCoreIndex) << 20);
    }

    static Baseline ReadState(Cpu cpu)
    {
        var curve = new int[8];
        for (int i=0;i<8;i++)
        {
            uint? m = cpu.GetPsmMarginSingleCore(CoreMask(i));
            if (m is null) throw new InvalidOperationException("SMU read failed on core " + i);
            curve[i] = unchecked((int)m.Value);
        }
        return new Baseline(cpu.GetFMax(), curve);
    }

    static Baseline EnsureBaseline(Cpu cpu)
    {
        if (!File.Exists(BaselinePath))
            throw new InvalidOperationException("Captured cpu-baseline.json is required; refusing to capture an unknown running profile.");
        var baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(BaselinePath)) ?? throw new InvalidOperationException("Invalid CPU baseline");
        if (baseline.FMax != 4750 || baseline.Curve.Length != 8 || baseline.Curve.Any(v => v != 0)) throw new InvalidOperationException("Unexpected CPU baseline");
        return baseline;
    }

    static bool Apply(Cpu cpu, int curve)
    {
        bool ok = true;
        for(int i=0;i<8;i++) ok &= cpu.SetPsmMarginSingleCore(CoreMask(i), curve);
        return ok;
    }

    static bool Restore(Cpu cpu, Baseline b)
    {
        bool ok=true;
        for(int i=0;i<8;i++) ok &= cpu.SetPsmMarginSingleCore(CoreMask(i), b.Curve[i]);
        return ok;
    }

    static int Main(string[] args)
    {
        try
        {
            using var cpu = new Cpu();
            if (!System.Text.RegularExpressions.Regex.IsMatch(cpu.info.cpuName, @"\bRyzen 7 5700X\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                || cpu.info.topology.physicalCores != 8 || cpu.info.topology.logicalCores != 16)
                throw new InvalidOperationException("This build supports only Ryzen 7 5700X (8 cores / 16 threads).");
            string cmd = args.Length>0 ? args[0].ToLowerInvariant() : "query";
            if (cmd == "capture-baseline")
            {
                var captured = ReadState(cpu);
                if (captured.FMax != 4750 || captured.Curve.Any(v => v != 0))
                    throw new InvalidOperationException("Baseline capture requires FMax 4750 and CO 0 on all eight cores. Restore the intended stock configuration first.");
                using var file = new FileStream(BaselinePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(file, captured);
                Console.WriteLine(JsonSerializer.Serialize(new { profile=cmd, success=true, fmax=captured.FMax, curve=captured.Curve }));
                return 0;
            }
            var baseline = EnsureBaseline(cpu);
            bool ok=true;
            int co = 0;
            switch(cmd)
            {
                case "stock":
                    ok=Restore(cpu,baseline); break;
                case "high":
                    co=-5; ok=Apply(cpu,co); break;
                case "medium":
                    co=-10; ok=Apply(cpu,co); break;
                case "max":
                    co=-15; ok=Apply(cpu,co); break;
                case "query": break;
                default: Console.Error.WriteLine("unknown profile"); return 2;
            }
            var s=ReadState(cpu);
            Console.WriteLine(JsonSerializer.Serialize(new {
                profile=cmd, success=ok, fmax=s.FMax, curve=s.Curve,
                baselineFmax=baseline.FMax, baselineCurve=baseline.Curve,
                temperature=cpu.GetCpuTemperature(), ccdTemperature=cpu.GetSingleCcdTemperature(0),
                cpu=cpu.info.cpuName, codename=cpu.info.codeName
            }));
            return ok?0:1;
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 3;
        }
    }
}

