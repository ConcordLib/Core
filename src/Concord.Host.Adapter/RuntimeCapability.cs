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

    /// <summary>The runtime config key that turns IL access checks off. RimWorld 1.7 ships it enabled.</summary>
    public const string AccessCheckKey = "WaylithClr.SkipAccessChecks";

    /// <summary>The environment variable form of <see cref="AccessCheckKey" />.</summary>
    public const string AccessCheckSwitch = "DOTNET_SkipAccessChecks";

    private const string SupportFlag = "IsSupported";

    private const string ReplaceEntry = "ReplaceMethodBody";

    private const string RevertEntry = "Revert";

    private const string RuntimeSwitch = "DOTNET_MethodBodyUpdater";

    private const int MethodSpecTable = 0x2B;

    private static readonly (BodyReplacement Support, string? Failure) Once =
        Probe(Updater(), ForkMinter.IsAvailable);

    private static readonly bool SkipsOnce = SkipsAccessChecks(
        AppContext.GetData(AccessCheckKey),
        Environment.GetEnvironmentVariable(AccessCheckSwitch));

    private static readonly bool MintsOnce = ProbeMethodSpecs();

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

    /// <summary>
    ///     <see langword="true" /> when this runtime has IL access checks switched off, so a body replaced in
    ///     place may name a private or internal member of another assembly and the call still runs.
    /// </summary>
    public static bool IgnoresAccessChecks => SkipsOnce;

    /// <summary>
    ///     <see langword="true" /> when a generic method operand can be named in a replaced body. Where it is
    ///     <see langword="false" /> the MethodSpec is malformed, published anyway, and throws on first compile.
    /// </summary>
    public static bool MintsMethodSpecs => MintsOnce;

    /// <summary>This runtime's own description, as it appears in every message this type produces.</summary>
    public static string Runtime => RuntimeInformation.FrameworkDescription;

    /// <summary>
    ///     Judges whether access checks are off. The pure form behind <see cref="IgnoresAccessChecks" />, so a
    ///     caller can pass stand-in values instead of this process's own configuration.
    /// </summary>
    /// <param name="configured">The <see cref="AccessCheckKey" /> runtime config value, boxed as the host set it.</param>
    /// <param name="environment">The <see cref="AccessCheckSwitch" /> environment value, or <see langword="null" />.</param>
    /// <returns><see langword="true" /> when either source turns the checks off.</returns>
    public static bool SkipsAccessChecks(object? configured, string? environment) {
        if (configured is bool set) {
            return set;
        }

        if (configured is string text && bool.TryParse(text, out bool parsed)) {
            return parsed;
        }

        return environment == "1" || string.Equals(environment, "true", StringComparison.OrdinalIgnoreCase);
    }

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
                $"'{UpdaterTypeName}' reports {SupportFlag} on '{Runtime}', and it "
                + (MintsMethodSpecs
                    ? "mints a usable MethodSpec, so a body may name a generic method"
                    : "mints a malformed MethodSpec, so a target whose body names a generic method stays on the "
                      + "detour backend"),
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

    private static bool ProbeMethodSpecs() {
        if (!ForkMinter.IsAvailable) {
            return false;
        }

        try {
            MethodInfo generic = typeof(RuntimeCapability).GetMethod(
                nameof(Generic),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            int definitionToken = new ForkMinter().Token(typeof(RuntimeCapability).Module, generic);
            return (definitionToken >>> 24) == MethodSpecTable;
        } catch (Exception thrown) when (thrown is not OutOfMemoryException) {
            return false;
        }
    }

    private static T Generic<T>(T value) {
        return value;
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
