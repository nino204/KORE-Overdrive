using System.Runtime.InteropServices;
namespace KOREOverdrive;
internal sealed class CpuUsage
{
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    ulong previousIdle,previousTotal;
    bool initialized;
    public double? Read()
    {
        if(!GetSystemTimes(out ulong idle,out ulong kernel,out ulong user))return null;
        ulong total=kernel+user;
        if(!initialized){previousIdle=idle;previousTotal=total;initialized=true;return null;}
        ulong elapsed=total-previousTotal, idleElapsed=idle-previousIdle;
        previousIdle=idle;previousTotal=total;
        return elapsed==0?null:Math.Clamp(100.0*(1.0-(double)idleElapsed/elapsed),0,100);
    }
}
