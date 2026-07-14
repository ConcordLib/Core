using System.Reflection;
using System.Text;

namespace Concord.Detour;

internal static class PatchDebugLog {
    private const string FileName = "Concord.PatchDebug.log";
    private static readonly object Gate = new object();

    internal static void Append(MethodBase target, string il) {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop)) {
            throw new DirectoryNotFoundException("[PatchDebug] could not resolve the current user's desktop directory.");
        }

        Directory.CreateDirectory(desktop);
        Append(Path.Combine(desktop, FileName), target, il);
    }

    internal static void Append(string logPath, MethodBase target, string il) {
        StringBuilder entry = new StringBuilder();
        entry.Append("=== ")
            .Append(target.DeclaringType?.FullName ?? "<unknown>")
            .Append('.')
            .Append(target.Name)
            .AppendLine(" ===");
        entry.Append(il);
        if (il.Length == 0 || il[il.Length - 1] != '\n') {
            entry.AppendLine();
        }

        entry.AppendLine();

        lock (Gate) {
            File.AppendAllText(logPath, entry.ToString());
        }
    }
}
