using System.Reflection;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using ReflectedBody = System.Reflection.MethodBody;

namespace Concord.Host.Adapter.Tests;

public class StackBoundCorpusTests {
    private const int Reported = 24;

    private readonly ITestOutputHelper output;

    public StackBoundCorpusTests(ITestOutputHelper output) {
        this.output = output;
    }

    [Theory]
    [MemberData(nameof(RealBodies.Corpora), MemberType = typeof(RealBodies))]
    public void TheBoundMatchesTheMaxstackTheCompilerWroteForEveryFatHeaderedBody(string corpus) {
        Tally tally = Measure(corpus);

        output.WriteLine(tally.Describe());

        Assert.True(tally.Bodies > 1000, "corpus '" + corpus + "' only decoded " + tally.Bodies + " bodies, too few to prove anything");
        Assert.True(
            tally.Over == 0,
            "the bound is ABOVE the maxstack the compiler declared on " + tally.Over + " of " + tally.Judged + " judgeable bodies\n" + tally.Describe());
        Assert.True(
            tally.Equal * 1000 >= tally.Judged * 995,
            "only " + tally.Equal + " of " + tally.Judged + " judgeable bodies match the declared maxstack exactly, "
            + "so the walk is losing a path a compiler saw\n" + tally.Describe());
    }

    [Theory]
    [MemberData(nameof(RealBodies.Corpora), MemberType = typeof(RealBodies))]
    public void NoBodyWhoseHeaderCouldBeTinyNeedsMoreThanTheTinyCeiling(string corpus) {
        Tally tally = Measure(corpus);

        Assert.True(
            tally.OverTinyCeiling == 0,
            "a body whose header reports the tiny-format constant of 8 needs more than 8 slots\n" + tally.Describe());
    }

    private static Tally Measure(string corpus) {
        Tally tally = new Tally();
        foreach (Assembly assembly in RealBodies.Of(corpus)) {
            foreach (MethodBase method in RealBodies.Methods(assembly)) {
                ComposedBody? body = RealBodies.Read(method, out _, out ReflectedBody? header);
                if (body is null || header is null) {
                    continue;
                }

                tally.Add(method, StackBound.Compute(body).Bound, header);
            }
        }

        return tally;
    }

    private sealed class Tally {
        private readonly List<string> disagreements = [];

        internal int Bodies { get; private set; }

        internal int Judged { get; private set; }

        internal int Ambiguous { get; private set; }

        internal int Over { get; private set; }

        internal int Equal { get; private set; }

        internal int Under { get; private set; }

        internal int OverTinyCeiling { get; private set; }

        internal void Add(MethodBase method, int bound, ReflectedBody header) {
            Bodies++;
            int declared = header.MaxStackSize;
            if (CouldBeTiny(header) && declared == 8) {
                Ambiguous++;
                if (bound > 8) {
                    OverTinyCeiling++;
                    Record(method, bound, declared, "over the tiny ceiling");
                }

                return;
            }

            Judged++;
            if (bound == declared) {
                Equal++;
                return;
            }

            if (bound > declared) {
                Over++;
                Record(method, bound, declared, "over");
            }
            else {
                Under++;
                Record(method, bound, declared, "UNDER");
            }
        }

        internal string Describe() {
            StringBuilder report = new StringBuilder();
            report.Append("bodies ").Append(Bodies)
                .Append(", judgeable ").Append(Judged)
                .Append(" (equal ").Append(Equal)
                .Append(", over ").Append(Over)
                .Append(", under ").Append(Under)
                .Append("), tiny-header ambiguous ").Append(Ambiguous)
                .Append(" (over the ceiling of 8: ").Append(OverTinyCeiling).Append(')');

            foreach (string line in disagreements) {
                report.Append('\n').Append("  ").Append(line);
            }

            if (Over + Under + OverTinyCeiling > disagreements.Count) {
                report.Append("\n  ... ").Append(Over + Under + OverTinyCeiling - disagreements.Count).Append(" more");
            }

            return report.ToString();
        }

        private static bool CouldBeTiny(ReflectedBody header) {
            return header.LocalVariables.Count == 0
                && header.ExceptionHandlingClauses.Count == 0
                && header.GetILAsByteArray() is { Length: < 64 };
        }

        private void Record(MethodBase method, int bound, int declared, string direction) {
            if (disagreements.Count >= Reported) {
                return;
            }

            disagreements.Add(
                direction + ": bound " + bound + " vs header " + declared + "  "
                + (method.DeclaringType?.FullName ?? "?") + "." + method.Name);
        }
    }
}
