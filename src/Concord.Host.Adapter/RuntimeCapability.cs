using System.Reflection;
using System.Runtime.InteropServices;

namespace Concord.Host.Adapter;

/// <summary>
///     What the probe found when it asked this runtime whether a method body can be replaced in place. Every
///     value but <see cref="Available" /> is a reason for the caller to keep the detour backend.
/// </summary>
public enum BodyReplacement {
    /// <summary>The api is present and enabled and the token minter bound, so composed bodies can be written.</summary>
    Available,

    /// <summary>
    ///     The updater type is not on this runtime. Ordinary on a stock runtime, not a fault: body replacement
    ///     exists only in the forked CoreLib that RimWorld 1.7 ships.
    /// </summary>
    ApiAbsent,

    /// <summary>
    ///     The type exists but does not carry the members this backend was built against, so nothing is
    ///     attempted through it.
    /// </summary>
    ApiUnrecognized,

    /// <summary>
    ///     The api reports itself switched off, which is what <c>DOTNET_MethodBodyUpdater=0</c> does. The
    ///     backend stays off with it.
    /// </summary>
    DisabledByRuntime,

    /// <summary>
    ///     Replacement is available but the metadata-token entry points did not bind, so composed IL could not
    ///     name the members it calls.
    /// </summary>
    MintingAbsent,

    /// <summary>
    ///     Reading the support flag threw, so the managed api and the native runtime under it do not match.
    ///     The detail is kept in <see cref="RuntimeCapability.ProbeFailure" />.
    /// </summary>
    ProbeFailed,
}

/// <summary>
///     Answers whether this runtime can replace a method body in place. Probed once per process and never
///     re-read; a caller picking a backend reads <see cref="IsAvailable" /> and logs <see cref="Explain()" />.
/// </summary>
public static class RuntimeCapability {
    /// <summary>The assembly-qualified name of the runtime type this backend binds to.</summary>
    public const string UpdaterTypeName = "WaylithClr.MethodBodyUpdater, System.Private.CoreLib";

    private const string SupportFlag = "IsSupported";

    private const string ReplaceEntry = "ReplaceMethodBody";

    private const string RevertEntry = "Revert";

    private const string RuntimeSwitch = "DOTNET_MethodBodyUpdater";

    private static readonly (BodyReplacement Support, string? Failure) Once =
        Probe(Updater(), ForkMinter.IsAvailable);

    /// <summary>The one-shot verdict for this process.</summary>
    public static BodyReplacement Support => Once.Support;

    /// <summary>
    ///     The exception type and message captured while probing, <see langword="null" /> when nothing threw.
    ///     Only <see cref="BodyReplacement.ProbeFailed" /> carries one.
    /// </summary>
    public static string? ProbeFailure => Once.Failure;

    /// <summary>
    ///     <see langword="true" /> only when bodies can be replaced here. It is <see langword="false" /> on a
    ///     stock runtime, which is expected rather than an error.
    /// </summary>
    public static bool IsAvailable => Support == BodyReplacement.Available;

    /// <summary>This runtime's own description, as it appears in every message this type produces.</summary>
    public static string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>Explains this process's verdict, including the probe failure detail when there is one.</summary>
    /// <returns>A log-ready sentence naming the api, this runtime, and why replacement is or is not usable.</returns>
    public static string Explain() {
        return Explain(Support, ProbeFailure);
    }

    /// <summary>Looks the runtime's updater type up by name without throwing when it is absent.</summary>
    /// <returns>The updater type, or <see langword="null" /> on any runtime that does not ship it.</returns>
    public static Type? Updater() {
        return Type.GetType(UpdaterTypeName, throwOnError: false);
    }

    /// <summary>
    ///     Judges a candidate updater type. The pure form behind <see cref="Support" />, so a caller can pass
    ///     a stand-in type to see what verdict a given api shape produces.
    /// </summary>
    /// <param name="updater">The type to judge, or <see langword="null" /> to report the api absent.</param>
    /// <param name="mintingBound">Whether the token entry points bound, normally <see cref="ForkMinter.IsAvailable" />.</param>
    /// <returns>The verdict, plus the exception detail when the probe threw and <see langword="null" /> otherwise.</returns>
    public static (BodyReplacement Support, string? Failure) Probe(Type? updater, bool mintingBound) {
        if (updater is null) {
            return (BodyReplacement.ApiAbsent, null);
        }

        object? flag;
        try {
            flag = updater.GetProperty(SupportFlag, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        } catch (Exception thrown) {
            Exception cause = (thrown as TargetInvocationException)?.InnerException ?? thrown;
            return (BodyReplacement.ProbeFailed, cause.GetType().Name + ": " + cause.Message);
        }

        if (flag is not bool supported) {
            return (BodyReplacement.ApiUnrecognized, null);
        }

        if (!supported) {
            return (BodyReplacement.DisabledByRuntime, null);
        }

        if (!Declares(updater, ReplaceEntry) || !Declares(updater, RevertEntry)) {
            return (BodyReplacement.ApiUnrecognized, null);
        }

        return (mintingBound ? BodyReplacement.Available : BodyReplacement.MintingAbsent, null);
    }

    /// <summary>Explains a verdict held without a probe failure detail.</summary>
    /// <param name="support">The verdict to explain.</param>
    /// <returns>A log-ready sentence naming the api and this runtime.</returns>
    /// <exception cref="NotSupportedException">The verdict is not a declared <see cref="BodyReplacement" /> value.</exception>
    public static string Explain(BodyReplacement support) {
        return Explain(support, null);
    }

    /// <summary>Explains a verdict, folding in the detail captured when the probe threw.</summary>
    /// <param name="support">The verdict to explain.</param>
    /// <param name="failure">The probe failure detail, read only for <see cref="BodyReplacement.ProbeFailed" />.</param>
    /// <returns>A log-ready sentence naming the api, this runtime, and what the verdict costs the caller.</returns>
    /// <exception cref="NotSupportedException">The verdict is not a declared <see cref="BodyReplacement" /> value.</exception>
    public static string Explain(BodyReplacement support, string? failure) {
        return support switch {
            BodyReplacement.Available =>
                $"'{UpdaterTypeName}' reports {SupportFlag} on '{Runtime}'",
            BodyReplacement.ApiAbsent =>
                $"'{UpdaterTypeName}' is not present on '{Runtime}', so method bodies cannot be replaced here",
            BodyReplacement.ApiUnrecognized =>
                $"'{UpdaterTypeName}' is present on '{Runtime}' but does not expose a static bool {SupportFlag} " +
                $"plus {ReplaceEntry} and {RevertEntry}, so this is not the api this backend was built against",
            BodyReplacement.DisabledByRuntime =>
                $"'{UpdaterTypeName}'.{SupportFlag} is false on '{Runtime}', which is what {RuntimeSwitch}=0 does. " +
                "The runtime's own switch turns body replacement off, so the backend stays off with it",
            BodyReplacement.MintingAbsent =>
                $"'{UpdaterTypeName}' reports {SupportFlag} on '{Runtime}' but its metadata-token entry points " +
                "did not bind, so composed IL could not name anything it calls",
            BodyReplacement.ProbeFailed =>
                $"reading '{UpdaterTypeName}'.{SupportFlag} on '{Runtime}' threw, so the managed api and the " +
                "native runtime under it do not match: " + (failure ?? "no detail was captured"),
            _ => throw new NotSupportedException(support + " has no explanation"),
        };
    }

    private static bool Declares(Type updater, string entry) {
        foreach (MethodInfo method in updater.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            if (method.Name == entry) {
                return true;
            }
        }

        return false;
    }
}
