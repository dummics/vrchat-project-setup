using System.Text;

namespace VrcSetup.Infrastructure;

public sealed class DesktopInstanceLease : IDisposable
{
    private readonly FileStream stream;
    private DesktopInstanceLease(FileStream stream) => this.stream = stream;
    public static DesktopInstanceLease? TryAcquire(string home, out int ownerPid)
    {
        Directory.CreateDirectory(home); ownerPid = 0;
        var path = Path.Combine(home, "desktop.lock");
        FileStream file;
        try { file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        {
            try { using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var reader = new StreamReader(input); int.TryParse(reader.ReadToEnd(), out ownerPid); }
            catch (IOException) { }
            return null;
        }
        try
        {
            file.SetLength(0); var data = Encoding.UTF8.GetBytes(Environment.ProcessId.ToString());
            file.Write(data); file.Flush(true); return new(file);
        }
        catch { file.Dispose(); throw; }
    }
    public void Dispose() => stream.Dispose();
}
