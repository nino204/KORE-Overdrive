using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace KOREOverdrive;
internal static class Program
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string StateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KOREOverdrive");
    public static void Log(string text)
    {
        Directory.CreateDirectory(StateDir);
        File.AppendAllText(Path.Combine(StateDir, "hardware.log"), $"{DateTimeOffset.Now:O} {text}\n");
    }
    static async Task Main(string[] args)
    {
        if (args.Contains("--self-test")) { SelfTest(); return; }
        using var singleton = new Mutex(true, @"Global\KOREOverdrive.Backend", out bool first);
        if (!first) return;
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) { Log("Backend exige elevacao"); return; }
        try
        {
            Directory.CreateDirectory(StateDir);
            var tokenPath = Path.Combine(StateDir, "api-token.txt");
            string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(tokenPath, token);
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(tokenPath).SetAccessControl(security);
            using var hardware = new Hardware();
            await hardware.Initialize();
            using var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:17863/");
            listener.Start();
            Log("Backend pronto; 127.0.0.1:17863; nenhuma escrita de perfil no startup");
            _ = hardware.PollLoop();
            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = Handle(context, token, hardware);
            }
        }
        catch (Exception ex) { Log(ex.ToString()); }
    }
    static async Task Handle(HttpListenerContext c, string token, Hardware hw)
    {
        try
        {
            if (c.Request.Headers["X-Kore-Token"] != token || c.Request.Headers["Origin"] is not null)
            { c.Response.StatusCode = 403; return; }
            string path = c.Request.Url!.AbsolutePath;
            object result;
            if (c.Request.HttpMethod == "GET" && path == "/state") result = hw.State;
            else if (c.Request.HttpMethod == "POST" && path == "/emergency") result = await hw.Emergency();
            else if (c.Request.HttpMethod == "POST" && path.StartsWith("/profile/") && int.TryParse(path[9..], out int level) && level is >= 0 and <= 3)
                result = await hw.Apply(level, "Clique / API");
            else { c.Response.StatusCode = 404; return; }
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
            c.Response.ContentType = "application/json; charset=utf-8";
            c.Response.Headers["Cache-Control"] = "no-store";
            c.Response.ContentLength64 = bytes.Length;
            await c.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception ex) { Log("API " + ex); c.Response.StatusCode = 500; }
        finally { c.Response.Close(); }
    }
    static void SelfTest()
    {
        void Check(bool test, string name) { if (!test) throw new Exception(name); Console.WriteLine("PASS " + name); }
        Check(Profiles.ThermalTarget(88, 35, 3) == 0, "CPU emergencia");
        Check(Profiles.ThermalTarget(40, 88, 1) == 0, "GPU emergencia");
        Check(Profiles.ThermalTarget(85, 40, 3) == 2, "CPU reduz nivel");
        Check(Profiles.ThermalTarget(40, 83, 2) == 1, "GPU reduz nivel");
        Check(Profiles.ThermalTarget(84.9, 82.9, 3) is null, "Resfriar nao aumenta perfil");
        Check(Profiles.CpuLevel(new(true,4750,[-15,-15,-15,-15,-15,-15,-15,-10],4750,new int[8])) is null, "CO misto entre nucleos");
        Check(Profiles.CpuLevel(new(true,4750,new int[7],4750,new int[8])) is null, "Sete nucleos nao confirmam CPU");
        Check(Profiles.CpuLevel(new(false,4750,new int[8],4750,new int[8])) is null, "SMU falhou nao e Stock");
        var loadedGpu = new GpuState(1650,7501,185,187,72,100,98,true,false);
        Check(Profiles.GpuValidation(3,loadedGpu,0).Contains("POTÊNCIA") && !Profiles.GpuValidation(3,loadedGpu,0).Contains("IDLE"), "FurMark limitado por potencia nao e idle");
        Check(Profiles.GpuValidation(3,loadedGpu with { Clock=2100 },2).Contains("CONFIRMANDO"), "Duas amostras nao confirmam clock");
        Check(Profiles.GpuValidation(3,loadedGpu with { Clock=2100 },3)=="APLICADO E CONFIRMADO PELO HARDWARE", "Tres leituras exatas sob carga confirmam clock");
        Check(Profiles.GpuValidation(3,loadedGpu with { Utilization=2 },0).Contains("IDLE"), "Idle fica pendente mesmo com clock observado");
    }
}
internal sealed class Hardware : IDisposable
{
    readonly SemaphoreSlim gate = new(1);
    readonly Computer pc = new() { IsGpuEnabled = true };
    readonly string cpuTuner = Path.Combine(AppContext.BaseDirectory, "../CpuTuner/KORECpuTuner.exe");
    readonly string smi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
    readonly List<string> errors = new();
    readonly CpuUsage cpuUsage = new();
    Process? boardProcess;
    int? requested;
    bool applying, emergencyRequested;
    int loadMatches;
    bool dynamicObserved;
    int? resetFirstClock;
    string cooling = "Detectando controle de fans", thermal = "Thermal Guard ativo — aguardando sensores";
    DateTimeOffset lastThermal = DateTimeOffset.MinValue;
    int? lastGuardLevel;
    public Snapshot State { get; private set; } = new(DateTimeOffset.Now,null,null,null,null,null,null,"NÃO CONFIRMADO","Detectando hardware","Aguardando sensores","Aguardando","",false,[],0,false);
    public async Task Initialize()
    {
        string gpuName = (await Command(smi, "-i", "0", "--query-gpu=name", "--format=csv,noheader")).Trim();
        if (!string.Equals(gpuName, "NVIDIA GeForce RTX 3060", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Esta versão exige NVIDIA GeForce RTX 3060 no índice 0; GPU detectada: " + gpuName);
        try { pc.Open(); } catch (Exception ex) { errors.Add("Controle GPU: " + ex.Message); }
        await Refresh();
    }
    public async Task PollLoop()
    {
        while (true)
        {
            var start = Stopwatch.StartNew();
            await gate.WaitAsync();
            try
            {
                await Refresh();
                int effective = Math.Max(requested ?? 0, State.CpuLevel ?? 0);
                if (State.Gpu is { Limit: > 170.6 } && effective == 0) effective = State.GpuObservedLevel ?? 1;
                int? target = Profiles.ThermalTarget(State.CpuTemperature, State.Gpu?.Temp, effective);
                if (effective > 0 && (State.CpuTemperature is null || State.Gpu is null))
                { target = 0; thermal = "SENSOR INDISPONÍVEL — proteção retorna Stock"; }
                if (target is not null && (target == 0 ? effective > 0 : (DateTimeOffset.Now-lastThermal).TotalSeconds >= 15))
                {
                    thermal = $"EVENTO TÉRMICO: CPU {State.CpuTemperature:0.0} °C / GPU {State.Gpu?.Temp:0} °C → {Profiles.Names[target.Value]}";
                    lastThermal = DateTimeOffset.Now; lastGuardLevel = target;
                    Program.Log(thermal);
                    await SetFans(true);
                    await ApplyCore(target.Value, "Thermal Guard");
                }
            }
            catch (Exception ex) { Program.Log("Poll " + ex); errors.Add(ex.Message); State = State with { Errors = errors.ToArray() }; }
            finally { gate.Release(); }
            int delay = Math.Max(100, 2000 - (int)start.ElapsedMilliseconds);
            await Task.Delay(delay);
        }
    }
    public async Task<Snapshot> Apply(int level, string reason)
    {
        await gate.WaitAsync();
        try
        {
            if (emergencyRequested && level > 0) return State;
            await Refresh();
            if (level > 0 && (State.CpuTemperature is null || State.Gpu is null))
                throw new InvalidOperationException("Temperatura real indisponível; perfil de desempenho bloqueado");
            if (level > 0 && Profiles.ThermalTarget(State.CpuTemperature, State.Gpu?.Temp, level) is not null)
                throw new InvalidOperationException("Temperatura acima do limite; aplique Stock");
            return await ApplyCore(level, reason);
        }
        catch (Exception ex)
        {
            Program.Log("Aplicacao falhou " + ex);
            if (level > 0) { try { await ApplyCore(0,"Rollback de falha"); } catch (Exception restore) { errors.Add("Rollback: "+restore.Message); } }
            errors.Add(ex.Message);
            State = State with { Validation = "NÃO CONFIRMADO", Errors = errors.ToArray(), Busy = false };
            return State;
        }
        finally { gate.Release(); }
    }
    public async Task<Snapshot> Emergency()
    {
        emergencyRequested = true;
        await gate.WaitAsync();
        try { thermal = "EMERGENCY STOCK acionado"; lastGuardLevel = 0; return await ApplyCore(0,"Emergency Stock"); }
        finally { emergencyRequested = false; gate.Release(); }
    }
    async Task<Snapshot> ApplyCore(int level, string reason)
    {
        applying = true;
        errors.Clear(); requested = level; loadMatches = 0; dynamicObserved = false; resetFirstClock = null;
        State = State with { Busy = true, Validation = "Aplicando e lendo o hardware...", RequestedLevel = level };
        Program.Log($"APPLY {level} {reason}");
        try
        {
            if (level > 0) await SetFans(true);
            if (level == 0) await Attempt(cpuTuner, "stock");
            else await Command(cpuTuner, Profiles.Commands[level]);
            if (emergencyRequested && level > 0) return await ApplyEmergencyAfterInterrupted();
            if (level == 0)
            {
                // Every restoration is attempted, even when one driver operation fails.
                await Attempt(smi, "-i", "0", "-rgc");
                await Attempt(smi, "-i", "0", "-pl", "170");
                await Attempt("powercfg.exe", "-setactive", "SCHEME_BALANCED");
                await SetFans(false);
            }
            else
            {
                await Command(smi, "-i", "0", "-pl", "187");
                if (emergencyRequested) return await ApplyEmergencyAfterInterrupted();
                await Command(smi, "-i", "0", "-lgc", $"{Profiles.Clock[level]},{Profiles.Clock[level]}");
                await Command("powercfg.exe", "-setactive", "SCHEME_MIN");
            }
            await Refresh();
            Program.Log("READBACK " + JsonSerializer.Serialize(State, Program.Json));
            return State;
        }
        finally { applying = false; State = State with { Busy = false }; }
    }
    async Task<Snapshot> ApplyEmergencyAfterInterrupted()
    { Program.Log("Perfil interrompido por Emergency Stock"); return await ApplyCore(0,"Emergency interrupt"); }
    async Task Attempt(string file, params string[] args)
    { try { await Command(file,args); } catch(Exception ex) { errors.Add(ex.Message); Program.Log(ex.Message); } }
    async Task SetFans(bool maximum)
    {
        BoardState? board = await Board(maximum ? "max" : "auto");
        bool cpuOk = board?.Fans.Length > 0 && board.Fans.All(f => maximum ? f.Maximum : f.BaselineMatches);
        int gpuControls = 0;
        try
        {
            foreach (var hardware in pc.Hardware)
            {
                hardware.Update();
                foreach (var sensor in hardware.Sensors.Where(s=>s.SensorType==SensorType.Control && s.Control is not null))
                { if (maximum) sensor.Control!.SetSoftware(100); else sensor.Control!.SetDefault(); gpuControls++; }
            }
        }
        catch (Exception ex) { errors.Add("Fan GPU: " + ex.Message); }
        cooling = $"CPU: {(cpuOk ? maximum ? "100% confirmado na curva Gigabyte" : "curva automática restaurada e relida" : "NÃO CONFIRMADO")} | GPU: {(gpuControls > 0 ? maximum ? "100% solicitado; verificar fan real" : "automático solicitado" : "controle indisponível")}";
        if (!cpuOk) errors.Add("Fan CPU sem confirmação: " + board?.Error);
        if (gpuControls == 0) errors.Add("Controle de fan GPU indisponível");
        Program.Log(cooling);
    }
    async Task Refresh()
    {
        CpuState? cpu = null; GpuState? gpu = null;
        var sampleErrors = new List<string>();
        // Hardware calls are serialized; no SMU access races with applying a profile.
        try
        {
            string raw = await Command(cpuTuner,"query");
            string line = raw.Split('\n').Last(s => s.TrimStart().StartsWith('{'));
            cpu = JsonSerializer.Deserialize<CpuState>(line,Program.Json);
            if (cpu is not { Success: true, Curve.Length: 8, BaselineCurve.Length: 8 } || cpu.Fmax != cpu.BaselineFmax || cpu.BaselineFmax != 4750 || cpu.BaselineCurve.Any(v=>v!=0))
                throw new Exception("SMU / baseline inválido; FMax precisa permanecer 4750 e baseline CO 0");
        }
        catch (Exception ex) { sampleErrors.Add("CPU: " + ex.Message); cpu = null; }
        try
        {
            string raw = await Command(smi,"-i","0","--query-gpu=clocks.gr,clocks.mem,power.draw,power.limit,temperature.gpu,fan.speed,utilization.gpu,clocks_event_reasons.sw_power_cap,clocks_event_reasons.hw_thermal_slowdown,clocks_event_reasons.sw_thermal_slowdown","--format=csv,noheader,nounits");
            string[] fields = raw.Trim().Split(',').Select(s=>s.Trim()).ToArray();
            double[] values = fields.Take(7).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
            if (fields.Length != 10 || values[4] <= 0) throw new Exception("Telemetria GPU inválida");
            gpu = new((int)values[0],(int)values[1],values[2],values[3],values[4],values[5],(int)values[6],fields[7]=="Active",fields[8]=="Active"||fields[9]=="Active");
        }
        catch (Exception ex) { sampleErrors.Add("GPU: " + ex.Message); }
        var board = await Board("read");
        if (board?.Error is not null) sampleErrors.Add("Gigabyte: " + board.Error);
        double? usage=cpuUsage.Read();
        double? temperature=cpu?.Temperature is > 0 and < 110 ? cpu.Temperature : null;
        string temperatureSource=temperature is not null ? "Tctl/Tdie — SMU/PawnIO" : "SMU/PawnIO indisponível";
        if (temperature is null) sampleErrors.Add("Temperatura direta da CPU indisponível; Gigabyte permanece como comparação");
        else if (board?.Temperature is double comparison && Math.Abs(comparison-temperature.Value)>8)
            temperatureSource += $" | Gigabyte {comparison:0} °C diverge; ignorado na proteção";
        string plan = "";
        try { plan = (await Command("powercfg.exe","-getactivescheme")).Trim(); } catch(Exception ex) { sampleErrors.Add(ex.Message); }
        int? cpuLevel = Profiles.CpuLevel(cpu), gpuLevel = null;
        if (gpu is not null)
        {
            if (Math.Abs(gpu.Limit-170)<0.6) gpuLevel=0;
            else if (Math.Abs(gpu.Limit-187)<0.6) for(int i=1;i<4;i++) if(gpu.Clock==Profiles.Clock[i]) gpuLevel=i;
        }
        if (requested is > 0)
        {
            if (gpu is not null && gpu.Utilization >= 20 && gpu.Clock == Profiles.Clock[requested.Value]) loadMatches++;
            else loadMatches = 0;
        }
        if (requested == 0 && gpu is not null)
        { resetFirstClock ??= gpu.Clock; if (gpu.Clock!=resetFirstClock) dynamicObserved=true; }
        string profile, validation;
        if (requested is int target)
        {
            profile = "ALVO: " + Profiles.Names[target];
            bool planOk=plan.Contains(target==0?"381b4222-f694-41f0-9685-ff5bb260df2e":"8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",StringComparison.OrdinalIgnoreCase);
            bool cpuOk=cpuLevel==target;
            bool powerOk=gpu is not null && Math.Abs(gpu.Limit-Profiles.Watts(target))<0.6;
            bool fanOk=board is {Fans.Length:>0} && board.Fans.All(f=>target==0?f.BaselineMatches:f.Maximum) && (target==0 || gpu?.Fan>=99);
            if (!cpuOk || !powerOk || !planOk || !fanOk || sampleErrors.Count>0 || errors.Count>0) validation="NÃO CONFIRMADO";
            else if (target==0) validation=dynamicObserved?"APLICADO E CONFIRMADO PELO HARDWARE":"CPU + 170 W CONFIRMADOS | CLOCK DINÂMICO — AGUARDANDO VARIAÇÃO";
            else validation=Profiles.GpuValidation(target,gpu!,loadMatches);
            if (cpuLevel is not null && !cpuOk || gpuLevel is not null && gpuLevel!=target && gpu?.Utilization>=20 && gpu is {PowerLimited:false,ThermalLimited:false}) validation="PERFIL MISTO";
        }
        else
        {
            profile=$"DETECTADO: CPU {(cpuLevel is int cl?Profiles.Names[cl]:"INDETERMINADA")} | GPU {(gpuLevel is int gl?Profiles.Names[gl]:"INDETERMINADA")} (clock observado)";
            validation=cpuLevel is not null && gpuLevel is not null && cpuLevel!=gpuLevel?"PERFIL MISTO":"NÃO CONFIRMADO — estado detectado; lock GPU a verificar sob carga";
        }
        string guard=lastGuardLevel is null?"Thermal Guard ativo — CPU 85 °C / GPU 83 °C; emergência 88 °C":thermal;
        if (temperature is null || gpu is null) guard="THERMAL GUARD SEM LEITURA COMPLETA — desempenho bloqueado";
        State=new(DateTimeOffset.Now,cpu,gpu,board,requested,cpuLevel,gpuLevel,validation,profile,guard,cooling,plan,applying,errors.Concat(sampleErrors).Distinct().ToArray(),loadMatches,dynamicObserved,usage,temperature,temperatureSource);
        string data=JsonSerializer.Serialize(State,Program.Json);
        await File.WriteAllTextAsync(Path.Combine(Program.StateDir,"latest.json.tmp"),data);
        File.Move(Path.Combine(Program.StateDir,"latest.json.tmp"),Path.Combine(Program.StateDir,"latest.json"),true);
        Program.Log("SAMPLE " + data);
    }
    async Task<BoardState?> Board(string action)
    {
        try
        {
            if (boardProcess is null || boardProcess.HasExited)
            {
                boardProcess?.Dispose();
                var info=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell/v1.0/powershell.exe"))
                {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardInputEncoding=new UTF8Encoding(false)};
                foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",Path.Combine(AppContext.BaseDirectory,"GigabyteBridge.ps1"),"-Action","daemon"})info.ArgumentList.Add(arg);
                boardProcess=Process.Start(info)??throw new Exception("Falha ao iniciar sensores Gigabyte");
                _=boardProcess.StandardError.ReadToEndAsync();
            }
            await boardProcess.StandardInput.WriteLineAsync(action);
            await boardProcess.StandardInput.FlushAsync();
            string raw=await boardProcess.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))??throw new Exception("Sensores Gigabyte encerraram");
            return JsonSerializer.Deserialize<BoardState>(raw,Program.Json);
        }
        catch(Exception ex) { try{boardProcess?.Kill(true);}catch{} boardProcess?.Dispose();boardProcess=null;return new(null,null,null,null,[],ex.Message); }
    }
    static async Task<string> Command(string file, params string[] args)
    {
        var info=new ProcessStartInfo(file) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(string arg in args) info.ArgumentList.Add(arg);
        using var process=Process.Start(info) ?? throw new Exception("Falha ao iniciar "+file);
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch(OperationCanceledException) { try { process.Kill(true); } catch {} throw new TimeoutException(Path.GetFileName(file)+" timeout"); }
        string stdout=await output, stderr=await error;
        if(process.ExitCode!=0) throw new Exception($"{Path.GetFileName(file)} ({process.ExitCode}): {stderr} {stdout}");
        return stdout;
    }
    public void Dispose() { try{boardProcess?.Kill(true);}catch{} boardProcess?.Dispose();pc.Close();gate.Dispose(); }
}
