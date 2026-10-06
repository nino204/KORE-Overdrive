namespace KOREOverdrive;
public record CpuState(bool Success, uint Fmax, int[] Curve, uint BaselineFmax, int[] BaselineCurve,
    double? Temperature = null, double? CcdTemperature = null);
public record GpuState(int Clock, int Memory, double Draw, double Limit, double Temp, double Fan, int Utilization,
    bool PowerLimited = false, bool ThermalLimited = false);
public record FanState(int Index, string Mode, bool Maximum, bool BaselineMatches);
public record BoardState(double? Temperature, double? Frequency, double? Vcore, double? FanRpm, FanState[] Fans, string? Error);
public record Snapshot(DateTimeOffset Timestamp, CpuState? Cpu, GpuState? Gpu, BoardState? Board,
    int? RequestedLevel, int? CpuLevel, int? GpuObservedLevel, string Validation, string Profile,
    string Thermal, string Cooling, string PowerPlan, bool Busy, string[] Errors, int LoadMatches, bool DynamicObserved,
    double? CpuUsage = null, double? CpuTemperature = null, string CpuTemperatureSource = "Aguardando validação");
public static class Profiles
{
    public static readonly string[] Names = ["STOCK", "ALTO DESEMPENHO", "OC MÉDIO", "DESEMPENHO MÁXIMO"];
    public static readonly string[] Commands = ["stock", "high", "medium", "max"];
    public static readonly int[] Co = [0, -5, -10, -15];
    public static readonly int[] Clock = [0, 1920, 1995, 2100];
    public static int Watts(int level) => level == 0 ? 170 : 187;
    public static int? CpuLevel(CpuState? cpu)
    {
        if (cpu is not { Success: true, Curve.Length: 8 }) return null;
        for (int i = 0; i < 4; i++) if (cpu.Curve.All(v => v == Co[i])) return i;
        return null;
    }
    public static int? ThermalTarget(double? cpu, double? gpu, int level)
    {
        if (cpu >= 88 || gpu >= 88) return 0;
        if (cpu >= 85 || gpu >= 83) return Math.Max(0, level - 1);
        return null;
    }
    public static string GpuValidation(int target, GpuState gpu, int exactSamples)
    {
        if (gpu.Utilization < 20) return "CPU + POWER LIMIT CONFIRMADOS | GPU EM IDLE — TESTAR SOB CARGA";
        if (gpu.Clock != Clock[target])
        {
            string reason = gpu.ThermalLimited ? " | LIMITADOR TÉRMICO ATIVO" : gpu.PowerLimited ? " | LIMITE DE POTÊNCIA ATIVO" : "";
            return $"NÃO CONFIRMADO — GPU SOB CARGA: {gpu.Clock} / {Clock[target]} MHz{reason}";
        }
        return exactSamples >= 3 ? "APLICADO E CONFIRMADO PELO HARDWARE" : "CPU + POWER LIMIT CONFIRMADOS | GPU SOB CARGA — CONFIRMANDO CLOCK";
    }
}
