using System;
using System.IO;

namespace Concord.Waylith.RimProbe;

public static class Report {
    public const string Prefix = "[RPROBE] ";

    private static readonly string Path = BuildPath();

    public static void Line(string text) {
        string stamped = Prefix + text;
        try {
            Console.Out.WriteLine(stamped);
            Console.Out.Flush();
        } catch (Exception) {
            Swallowed();
        }

        try {
            File.AppendAllText(Path, stamped + Environment.NewLine);
        } catch (Exception) {
            Swallowed();
        }
    }

    public static void Threw(string name, Exception thrown) {
        Exception cause = thrown;
        while (cause.InnerException != null) {
            cause = cause.InnerException;
        }

        Line(name + " THREW " + cause.GetType().FullName + ": " + Flatten(cause.Message));
    }

    public static void Step(string name, Action body) {
        try {
            body();
        } catch (Exception thrown) {
            Threw(name, thrown);
        }
    }

    public static string Flatten(string text) {
        return text == null ? "<null>" : text.Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string BuildPath() {
        string home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrEmpty(home) ? "waylith-rimprobe.log" : System.IO.Path.Combine(home, "waylith-rimprobe.log");
    }

    private static void Swallowed() {
    }
}
