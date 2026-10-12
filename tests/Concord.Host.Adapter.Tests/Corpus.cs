using System.Reflection;
using System.Text;

namespace Concord.Host.Adapter.Tests;

internal delegate void BodyCheck(MethodBase method, ComposedBody body, EncodedBody encoded, Tokens tokens);

internal sealed class CorpusResult {
    internal int Skipped { get; set; }

    internal int Read { get; set; }

    internal int Encoded { get; set; }

    internal int MethodSpecBodies { get; set; }

    internal int MethodSpecCalls { get; set; }

    internal int UnreachableCalls { get; set; }

    internal Dictionary<string, int> Refusals { get; } = [];

    internal List<string> Failures { get; } = [];

    internal string Summary {
        get {
            StringBuilder text = new StringBuilder();
            text.Append($"read {Read}, encoded {Encoded}, skipped {Skipped}, failures {Failures.Count}");
            text.Append($"\n  methodspec bodies {MethodSpecBodies} of {Read} read ({Percent(MethodSpecBodies, Read)}), methodspec calls {MethodSpecCalls}, unreachable calls {UnreachableCalls}");
            foreach (KeyValuePair<string, int> refusal in Refusals.OrderByDescending(entry => entry.Value)) {
                text.Append($"\n  refused {refusal.Value}: {refusal.Key}");
            }

            foreach (string failure in Failures) {
                text.Append($"\n  {failure}");
            }

            return text.ToString();
        }
    }

    private static string Percent(int part, int whole) {
        return whole == 0 ? "n/a" : (part * 100.0 / whole).ToString("0.0") + "%";
    }
}

internal static class Corpus {
    internal const int FailureSamples = 5;

    internal const string StandaloneSignatureOperand = "InlineSig";

    internal const string MethodSpecMarker = "MethodSpec token";

    internal static readonly string[] AllowedRefusals = [
        "a standalone-signature operand, which no token api can mint",
        "a function-pointer local",
    ];

    internal static CorpusResult Run(IEnumerable<Assembly> assemblies, BodyCheck check) {
        CorpusResult result = new CorpusResult();
        foreach (Assembly assembly in assemblies) {
            foreach (MethodBase method in RealBodies.Methods(assembly)) {
                ComposedBody? body = RealBodies.Read(method, out string? skipped);
                if (body is null) {
                    result.Skipped++;
                    if (skipped == StandaloneSignatureOperand) {
                        Count(result, AllowedRefusals[0]);
                    }
                    else if (skipped != "no body") {
                        Count(result, "read: " + skipped);
                    }

                    continue;
                }

                result.Read++;
                Probe(result, method, body);
                Tokens tokens = new Tokens();
                EncodedBody encoded;
                try {
                    encoded = BodyEncoder.Encode(body, tokens, new ComputedStack());
                }
                catch (Exception refused) {
                    Count(result, Bucket(refused));
                    continue;
                }

                result.Encoded++;
                try {
                    check(method, body, encoded, tokens);
                }
                catch (Exception failed) {
                    if (result.Failures.Count < FailureSamples) {
                        result.Failures.Add($"{method.DeclaringType}::{method.Name}: {failed.Message.ReplaceLineEndings(" ")}");
                    }
                }
            }
        }

        return result;
    }

    private static void Probe(CorpusResult result, MethodBase method, ComposedBody body) {
        WaylithTokenSource guard = new WaylithTokenSource(method.Module, Discard.Instance);
        bool carries = false;
        foreach (ComposedInstruction instruction in body.Instructions) {
            if (instruction.Instruction.operand is not MethodBase operand) {
                continue;
            }

            try {
                guard.TokenFor(operand);
            }
            catch (TokenAccessException refused) {
                if (refused.Message.Contains(MethodSpecMarker, StringComparison.Ordinal)) {
                    result.MethodSpecCalls++;
                    carries = true;
                }
                else {
                    result.UnreachableCalls++;
                }
            }
        }

        if (carries) {
            result.MethodSpecBodies++;
        }
    }

    private static void Count(CorpusResult result, string bucket) {
        result.Refusals[bucket] = result.Refusals.GetValueOrDefault(bucket) + 1;
    }

    private static string Bucket(Exception refused) {
        if (refused is NotSupportedException && refused.Message.Contains("standalone signature", StringComparison.Ordinal)) {
            return AllowedRefusals[0];
        }

        if (refused is ArgumentException && refused.Message.Contains("function-pointer local", StringComparison.Ordinal)) {
            return AllowedRefusals[1];
        }

        return refused.GetType().Name + ": " + refused.Message.ReplaceLineEndings(" ");
    }

    private sealed class Discard : IMetadataMinter {
        internal static readonly Discard Instance = new Discard();

        public int Token(Module target, MethodBase method) {
            return 0x0a000001;
        }

        public int Token(Module target, FieldInfo field) {
            return 0x04000001;
        }

        public int Token(Module target, Type type) {
            return 0x01000001;
        }

        public int Token(Module target, string literal) {
            return 0x70000001;
        }

        public int LocalsToken(Module target, byte[] signature) {
            return 0x11000001;
        }
    }
}
