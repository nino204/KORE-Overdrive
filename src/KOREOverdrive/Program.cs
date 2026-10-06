using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace KOREOverdrive;
internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex=new Mutex(true,@"Local\KOREOverdrive.UI",out bool first);
        if(!first) { try { using var signal=EventWaitHandle.OpenExisting(@"Local\KOREOverdrive.Show"); signal.Set(); } catch {} return; }
        RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        var app=new Application();
        app.DispatcherUnhandledException+=(_,e)=>{ File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KOREOverdrive","ui-errors.log"),e.Exception+"\n"); e.Handled=true; };
        app.Run(new Dashboard());
    }
}
public sealed class Dashboard : Window
{
    readonly TextBlock profile=Text("DETECTANDO HARDWARE...",18,true), validation=Text("Aguardando leitura real",16,true);
    readonly TextBlock cpu=Text("Lendo CPU...",15), gpu=Text("Lendo GPU...",15), thermal=Text("Thermal Guard — aguardando sensores",14,true);
    readonly TextBlock cooling=Text("Lendo fans...",14), power=Text("Lendo plano de energia...",13), status=Text("Iniciando helper de hardware...",13);
    readonly TextBlock errors=Text("",13), updated=Text("",12);
    readonly List<Button> buttons=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(2)};
    readonly HttpClient client=new(){BaseAddress=new Uri("http://127.0.0.1:17863/"),Timeout=TimeSpan.FromSeconds(45)};
    readonly EventWaitHandle showSignal=new(false,EventResetMode.AutoReset,@"Local\KOREOverdrive.Show");
    readonly string stateDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"KOREOverdrive");
    readonly JsonSerializerOptions json=new(JsonSerializerDefaults.Web);
    bool refreshing,applying;
    public Dashboard()
    {
        Title="KORE Overdrive"; Width=1060; Height=770; MinWidth=880; MinHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=new SolidColorBrush(Color.FromRgb(244,246,250));
        FontFamily=new FontFamily("Segoe UI");
        var body=new StackPanel{Margin=new Thickness(28)};
        body.Children.Add(Text("KORE OVERDRIVE",29,true));
        body.Children.Add(Text("Ryzen 7 5700X + RTX 3060 · controle e validação real de CPU + GPU",14));
        body.Children.Add(new Border{Height=18});
        body.Children.Add(profile); validation.Margin=new Thickness(0,8,0,12); body.Children.Add(validation);
        var profiles=new System.Windows.Controls.Primitives.UniformGrid{Columns=4};
        for(int i=0;i<4;i++) { int level=i; var button=Button(Profiles.Names[i],async()=>await Apply(level)); buttons.Add(button); profiles.Children.Add(button); }
        body.Children.Add(profiles);
        body.Children.Add(Card("CPU · Ryzen 7 5700X",cpu)); body.Children.Add(Card("GPU · NVIDIA RTX 3060",gpu));
        var guard=new StackPanel(); guard.Children.Add(thermal); guard.Children.Add(cooling); guard.Children.Add(power);
        body.Children.Add(new Border{Child=guard,Background=Brushes.White,Padding=new Thickness(16),Margin=new Thickness(0,10,0,10)});
        var actions=new System.Windows.Controls.Primitives.UniformGrid{Columns=4};
        actions.Children.Add(Button("VALIDAR AGORA",async()=>await Refresh()));
        actions.Children.Add(Button("OCCT",()=>{ LaunchTool("OCBase.OCCT.Personal_Microsoft.Winget.Source_8wekyb3d8bbwe/OCCT.exe"); return Task.CompletedTask; }));
        actions.Children.Add(Button("FURMARK 2",()=>{ LaunchTool("Geeks3D.FurMark.2_Microsoft.Winget.Source_8wekyb3d8bbwe/FurMark_win64/FurMark_GUI.exe"); return Task.CompletedTask; }));
        var emergency=Button("EMERGENCY STOCK",Emergency); emergency.Foreground=Brushes.DarkRed; emergency.FontWeight=FontWeights.Bold; actions.Children.Add(emergency);
        body.Children.Add(actions);
        var note=Text("CO −5 / −10 / −15 · FMax baseline 4750 MHz · Máximo: estabilidade ainda não certificada.\nStock restaura CO 0, GPU dinâmica, 170 W, plano Equilibrado e curva automática dos fans.",13);
        note.Margin=new Thickness(4,14,4,10); body.Children.Add(note);
        body.Children.Add(status); errors.Foreground=Brushes.DarkRed; errors.Margin=new Thickness(0,7,0,0); body.Children.Add(errors);
        body.Children.Add(updated);
        Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        Loaded+=async(_,_)=>await Initialize();
        timer.Tick+=async(_,_)=> { if(showSignal.WaitOne(0)){ if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal; Activate(); } if(!applying)await Refresh(); };
        Closed+=(_,_)=>{timer.Stop();client.Dispose();showSignal.Dispose();};
    }
    static TextBlock Text(string value,int size,bool bold=false)=>new(){Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=new SolidColorBrush(Color.FromRgb(24,38,64)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,3)};
    static Border Card(string title,TextBlock value)
    {
        var content=new StackPanel(); content.Children.Add(Text(title,16,true)); value.Margin=new Thickness(0,8,0,0); content.Children.Add(value);
        return new(){Child=content,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(218,224,233)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(18),Margin=new Thickness(0,12,0,0)};
    }
    Button Button(string title,Func<Task> action)
    {
        var button=new Button{Content=new TextBlock{Text=title,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center},MinHeight=48,Margin=new Thickness(4),Padding=new Thickness(8),FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(24,38,64)),Background=Brushes.White};
        button.Click+=async(_,_)=>{try{await action();}catch(Exception ex){status.Text="Falha: "+ex.Message;validation.Text="NÃO CONFIRMADO";validation.Foreground=Brushes.DarkRed;}};
        return button;
    }
    async Task Initialize()
    {
        try
        {
            await Task.Run(()=>{
                using var process=Process.Start(new ProcessStartInfo("schtasks.exe","/run /tn KOREOverdriveBackend"){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true});
                process?.WaitForExit(5000);
            });
            for(int i=0;i<15;i++) { if(await Refresh())break; await Task.Delay(1000); }
        }
        catch(Exception ex){status.Text="Helper: "+ex.Message;}
        timer.Start();
    }
    async Task<Snapshot> Request(string path,bool post=false)
    {
        string token=await File.ReadAllTextAsync(Path.Combine(stateDir,"api-token.txt"));
        using var request=new HttpRequestMessage(post?HttpMethod.Post:HttpMethod.Get,path);
        request.Headers.Add("X-Kore-Token",token.Trim());
        using var response=await client.SendAsync(request); response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<Snapshot>(await response.Content.ReadAsStringAsync(),json)??throw new Exception("Resposta inválida");
    }
    async Task<bool> Refresh()
    {
        if(refreshing)return false; refreshing=true;
        try{Render(await Request("state"));return true;}
        catch(Exception ex){validation.Text="NÃO CONFIRMADO — helper indisponível";validation.Foreground=Brushes.DarkRed;status.Text=ex.Message;return false;}
        finally{refreshing=false;}
    }
    async Task Apply(int level)
    {
        if(applying)return; applying=true;foreach(var button in buttons)button.IsEnabled=false;
        validation.Text="APLICANDO E LENDO O HARDWARE..."; validation.Foreground=Brushes.DarkGoldenrod;
        status.Text="Aplicando "+Profiles.Names[level]+"...";
        try{var state=await Request("profile/"+level,true);Render(state);status.Text="Leitura pós-escrita concluída. "+state.Validation;}
        finally{applying=false;foreach(var button in buttons)button.IsEnabled=true;}
    }
    async Task Emergency()
    {
        status.Text="EMERGENCY STOCK — interrompendo perfil e restaurando...";
        var result=await Request("emergency",true); Render(result);status.Text="Emergency Stock: "+result.Validation;
    }
    void Render(Snapshot s)
    {
        profile.Text=s.Profile;
        bool fresh=(DateTimeOffset.Now-s.Timestamp).TotalSeconds<8;
        validation.Text=fresh?s.Validation:"NÃO CONFIRMADO — telemetria desatualizada";
        validation.Foreground=fresh&&s.Validation=="APLICADO E CONFIRMADO PELO HARDWARE"?Brushes.DarkGreen:s.Validation.Contains("MISTO")||s.Validation.StartsWith("NÃO")?Brushes.DarkRed:Brushes.DarkGoldenrod;
        string Number(double? value,string format="0.0")=>value?.ToString(format)??"indisponível";
        cpu.Text=$"Uso total: {Number(s.CpuUsage)}%   ·   Temperatura Tctl/Tdie: {Number(s.CpuTemperature)} °C   ·   CCD: {Number(s.Cpu?.CcdTemperature)} °C\nFrequência: {Number(s.Board?.Frequency,"0")} MHz   ·   FMax: {s.Cpu?.Fmax.ToString()??"indisponível"} MHz   ·   Vcore: {Number(s.Board?.Vcore,"0.000")} V   ·   Fan CPU: {Number(s.Board?.FanRpm,"0")} RPM\nCurve Optimizer — 8 núcleos: {(s.Cpu is null?"indisponível":string.Join(" / ",s.Cpu.Curve))}\nFonte: {s.CpuTemperatureSource} · Placa-mãe: {Number(s.Board?.Temperature)} °C";
        gpu.Text=s.Gpu is null?"Telemetria GPU indisponível":$"Clock: {s.Gpu.Clock} MHz   ·   Memória: {s.Gpu.Memory} MHz   ·   Utilização: {s.Gpu.Utilization}%\nPotência: {s.Gpu.Draw:0.0} W   ·   Limite: {s.Gpu.Limit:0.0} W   ·   Temperatura: {s.Gpu.Temp:0} °C   ·   Fan: {s.Gpu.Fan:0}%\nAlvo: {(s.RequestedLevel is int l?l==0?"dinâmico / desbloqueado (verificar variação)":Profiles.Clock[l]+" MHz":"estado detectado; alvo não assumido")}   ·   Leituras exatas sob carga: {s.LoadMatches}\nLimitadores: {(s.Gpu.PowerLimited?"potência ATIVO":"potência livre")} · {(s.Gpu.ThermalLimited?"térmico ATIVO":"térmico livre")}";
        thermal.Text=s.Thermal;cooling.Text=s.Cooling;power.Text=s.PowerPlan;
        errors.Text=string.Join("\n",s.Errors.Select(e=>e.Length>350?e[..350]+"...":e));
        updated.Text=$"Atualizado {s.Timestamp.LocalDateTime:HH:mm:ss} · proteção permanece ativa com a janela fechada.";
    }
    void LaunchTool(string relative)
    {
        string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft/WinGet/Packages",relative);
        Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)!});
    }
}
