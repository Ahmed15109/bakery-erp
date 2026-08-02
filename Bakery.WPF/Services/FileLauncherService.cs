using System.Diagnostics;

namespace Bakery.WPF.Services;

public class FileLauncherService : IFileLauncherService
{
    public void OpenFile(string filePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = filePath,
            UseShellExecute = true
        };
        Process.Start(psi);
    }
}
