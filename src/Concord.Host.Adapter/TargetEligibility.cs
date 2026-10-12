using System.Collections.Concurrent;
using System.Reflection;

namespace Concord.Host.Adapter;

/// <summary>The answer <see cref="TargetEligibility.Judge" /> gives for a target: which backend can carry a patch on it, if any.</summary>
public enum TargetHandling {
    /// <summary>The composed body is published over the target, and callers already inlined or already jitted pick it up.</summary>
    Replace,

    /// <summary>A native detour carries the patch, so an already-inlined caller keeps running the original code.</summary>
    Detour,

    /// <summary>The target has no IL body to patch, so the caller surfaces <see cref="TargetVerdict.Code" /> and stops.</summary>
    Refuse,
}

/// <summary>Where a caller asks whether a target can take a replaced body, and where it gets the message to log when it has to fall back.</summary>
public static class TargetEligibility {
    /// <summary>The diagnostic code on a refusal, raised when the target has no IL body for any backend to work with.</summary>
    public const string NoBodyCode = "CONC168";

    /// <summary>The prefix on every fallback message, so a log reader can tell a backend downgrade from a patch failure.</summary>
    public const string FallbackMarker = "[Concord.Waylith] backend-fallback";

    private const string StillApplies =
        "The patch still applies through a native detour, without the inlined-caller and hot-caller guarantees " +
        "body replacement adds.";

    private static readonly ConcurrentDictionary<MethodBase, byte> Reported = new();

    /// <summary>Checks a target for the two things that rule body replacement out up front: a dynamic or collectible module, and no IL body at all.</summary>
    /// <param name="target">The method the caller wants to patch.</param>
    /// <returns>A <see cref="TargetHandling.Replace" /> verdict with empty code and message, a detour fallback, or a refusal carrying <see cref="NoBodyCode" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public static TargetVerdict Judge(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        if (target.IsCollectible || target.Module.Assembly.IsDynamic) {
            return Fallback(
                $"{Describe(target)} is declared in '{target.Module.Name}', a dynamic or collectible module. The " +
                $"runtime only versions method bodies in non-collectible modules. {StillApplies}");
        }

        if (NoBody(target) is not string reason) {
            return new TargetVerdict(TargetHandling.Replace, string.Empty, string.Empty);
        }

        string remedy = target.IsAbstract
            ? "Patch an override or an implementation instead."
            : "Patch the managed code that calls it instead.";

        return new TargetVerdict(
            TargetHandling.Refuse,
            NoBodyCode,
            $"{Describe(target)} {reason}, so the runtime has no IL body to replace and no Concord backend can " +
            $"patch it. {remedy}");
    }

    /// <summary>Builds the fallback verdict for a target whose composed body named something no metadata token can express, such as a generic method operand.</summary>
    /// <param name="target">The method whose body was rejected.</param>
    /// <param name="rejection">Why the body could not be encoded; a trailing period is trimmed before it goes into the message.</param>
    /// <returns>A <see cref="TargetHandling.Detour" /> verdict whose message starts with <see cref="FallbackMarker" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="rejection" /> is <c>null</c>, empty, or white space.</exception>
    public static TargetVerdict ForRejectedBody(MethodBase target, string rejection) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(rejection);

        return Fallback(
            $"{Describe(target)} composed a body no metadata token can express: {rejection.TrimEnd('.')}. " +
            StillApplies);
    }

    /// <summary>Builds the fallback verdict for a body the runtime itself threw on at publish time, folding the exception type and message into the explanation.</summary>
    /// <param name="target">The method whose publish attempt failed.</param>
    /// <param name="refusal">The exception the runtime threw.</param>
    /// <returns>A <see cref="TargetHandling.Detour" /> verdict whose message starts with <see cref="FallbackMarker" />.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public static TargetVerdict ForRuntimeRefusal(MethodBase target, Exception refusal) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(refusal);

        return Fallback(
            $"{Describe(target)} was refused by the runtime at publish time: " +
            $"{refusal.GetType().Name}, {refusal.Message.TrimEnd('.')}. {StillApplies}");
    }

    /// <summary>Gate for logging a fallback, so a target that falls back on every patch attempt is only announced the first time in a process.</summary>
    /// <param name="target">The method about to be reported.</param>
    /// <returns><c>true</c> the first time it is called for a target, and <c>false</c> on every call after that.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public static bool ReportOnce(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Reported.TryAdd(target, 0);
    }

    private static TargetVerdict Fallback(string message) {
        return new TargetVerdict(TargetHandling.Detour, string.Empty, FallbackMarker + " " + message);
    }

    private static string? NoBody(MethodBase target) {
        if (target.GetMethodBody() is not null) {
            return null;
        }

        if (target.IsAbstract) {
            return "is abstract";
        }

        if ((target.Attributes & MethodAttributes.PinvokeImpl) != 0) {
            return "is a p/invoke declaration";
        }

        MethodImplAttributes implementation = target.MethodImplementationFlags;
        if ((implementation & MethodImplAttributes.InternalCall) != 0) {
            return "is an InternalCall intrinsic";
        }

        if ((implementation & MethodImplAttributes.CodeTypeMask) == MethodImplAttributes.Runtime) {
            return "is provided by the runtime, not by IL";
        }

        return "has no IL body";
    }

    private static string Describe(MethodBase target) {
        string owner = target.DeclaringType?.FullName ?? target.Module.Name;
        return $"'{owner}.{target.Name}'";
    }
}

/// <summary>What <see cref="TargetEligibility" /> decided about one target, with the text a caller logs on a fallback or throws on a refusal.</summary>
public sealed class TargetVerdict {
    internal TargetVerdict(TargetHandling handling, string code, string message) {
        Handling = handling;
        Code = code;
        Message = message;
    }

    /// <summary>The outcome reached for this target, which decides whether the caller publishes a body, installs a detour, or stops.</summary>
    public TargetHandling Handling { get; }

    /// <summary>The Concord diagnostic code, or an empty string on any verdict other than <see cref="TargetHandling.Refuse" />.</summary>
    public string Code { get; }

    /// <summary>Why the verdict came out this way; empty on a clean <see cref="TargetHandling.Replace" /> and prefixed with <see cref="TargetEligibility.FallbackMarker" /> on a fallback.</summary>
    public string Message { get; }

    /// <summary>Raises the refusal as an exception carrying <see cref="Code" /> and <see cref="Message" />; a replace or detour verdict passes through untouched.</summary>
    /// <exception cref="ConcordEmitException">The verdict is <see cref="TargetHandling.Refuse" />.</exception>
    public void ThrowIfRefused() {
        if (Handling == TargetHandling.Refuse) {
            throw new ConcordEmitException(Code, Message);
        }
    }
}
