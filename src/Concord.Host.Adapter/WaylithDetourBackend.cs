using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;

namespace Concord.Host.Adapter;

/// <summary>
///     One row of the map the runtime uses to turn an offset in the written body back into an offset in the
///     target's own IL, which is what a stack trace line number is resolved against.
/// </summary>
public readonly struct OffsetPair {
    /// <summary>Initializes a new instance.</summary>
    /// <param name="original">The offset in the target's own IL.</param>
    /// <param name="replaced">The offset in the body that was written over it.</param>
    public OffsetPair(int original, int replaced) {
        Original = original;
        Replaced = replaced;
    }

    /// <summary>The offset in the target's own IL.</summary>
    public int Original { get; }

    /// <summary>The offset in the written body that maps back to <see cref="Original" />.</summary>
    public int Replaced { get; }
}

/// <summary>
///     Writes and takes back a method's IL body through the forked runtime's updater, bound by reflection.
///     A write is refused unless the revert entry bound too, so nothing is ever installed unrevertably.
/// </summary>
public sealed class ForkBodyWriter {
    private const string ReplaceEntry = "ReplaceMethodBody";

    private const string RevertEntry = "Revert";

    private readonly Action<MethodBase, byte[], Array>? replace;

    private readonly Action<MethodBase>? revert;

    private readonly Type? entry;

    private readonly ConstructorInfo? entryConstructor;

    /// <summary>Initializes a new instance bound to whatever updater this runtime offers, if any.</summary>
    public ForkBodyWriter() : this(RuntimeCapability.Updater()) {
    }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="updater">The updater type to bind, or <see langword="null" /> for a writer that cannot write.</param>
    public ForkBodyWriter(Type? updater) {
        MethodInfo? found = FindReplace(updater);
        if (found is null) {
            return;
        }

        entry = found.GetParameters()[2].ParameterType.GetGenericArguments()[0];
        entryConstructor = entry.GetConstructor([typeof(int), typeof(int)]);
        if (entryConstructor is null) {
            entry = null;
            return;
        }

        replace = BindReplace(found, entry);
        revert = updater!
            .GetMethod(RevertEntry, BindingFlags.Public | BindingFlags.Static, [typeof(MethodBase)])?
            .CreateDelegate<Action<MethodBase>>();
    }

    /// <summary>The process-wide writer every backend uses, so one reflection bind serves all of them.</summary>
    public static ForkBodyWriter Shared { get; } = new ForkBodyWriter();

    /// <summary>
    ///     <see langword="true" /> only when both write and revert bound. <see langword="false" /> means
    ///     every call on this writer throws, which is the normal state off the fork.
    /// </summary>
    public bool IsAvailable => replace is not null && revert is not null;

    /// <summary>Writes <paramref name="body" /> over <paramref name="target" />, with its offset map.</summary>
    /// <param name="target">The method whose body is replaced.</param>
    /// <param name="body">A complete ECMA-335 method body, header included.</param>
    /// <param name="offsetMap">Offset pairs for stack traces; an empty list means raw offsets get reported.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null" />.</exception>
    /// <exception cref="PlatformNotSupportedException">Write or revert did not bind, so the write is refused.</exception>
    public void Write(MethodBase target, byte[] body, IReadOnlyList<OffsetPair> offsetMap) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(offsetMap);

        if (replace is null || entryConstructor is null || entry is null) {
            throw Missing(ReplaceEntry);
        }

        if (revert is null) {
            throw new PlatformNotSupportedException(
                $"'{RuntimeCapability.UpdaterTypeName}'.{RevertEntry} did not bind on '{RuntimeCapability.Runtime}', " +
                $"so a body written through {ReplaceEntry} could never be taken back off. The write is refused " +
                "instead of published unrevertably, and the caller has to keep the detour backend.");
        }

        Array entries = Array.CreateInstance(entry, offsetMap.Count);
        for (int i = 0; i < offsetMap.Count; i++) {
            entries.SetValue(entryConstructor.Invoke([offsetMap[i].Original, offsetMap[i].Replaced]), i);
        }

        replace(target, body, entries);
    }

    /// <summary>Puts <paramref name="target" />'s own body back, undoing an earlier write.</summary>
    /// <param name="target">The method to restore.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    /// <exception cref="PlatformNotSupportedException">The revert entry did not bind on this runtime.</exception>
    public void Restore(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        (revert ?? throw Missing(RevertEntry))(target);
    }

    private static MethodInfo? FindReplace(Type? updater) {
        if (updater is null) {
            return null;
        }

        foreach (MethodInfo candidate in updater.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            if (candidate.Name != ReplaceEntry) {
                continue;
            }

            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length != 3 ||
                parameters[0].ParameterType != typeof(MethodBase) ||
                parameters[1].ParameterType != typeof(ReadOnlySpan<byte>) ||
                !parameters[2].ParameterType.IsGenericType ||
                parameters[2].ParameterType.GetGenericTypeDefinition() != typeof(ReadOnlySpan<>)) {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static Action<MethodBase, byte[], Array>? BindReplace(MethodInfo replace, Type entry) {
        Type entries = entry.MakeArrayType();
        MethodInfo? bytes = typeof(ReadOnlySpan<byte>).GetMethod("op_Implicit", [typeof(byte[])]);
        MethodInfo? map = typeof(ReadOnlySpan<>).MakeGenericType(entry).GetMethod("op_Implicit", [entries]);
        if (bytes is null || map is null) {
            return null;
        }

        DynamicMethod call = new DynamicMethod(
            "ConcordReplaceMethodBody",
            typeof(void),
            [typeof(MethodBase), typeof(byte[]), typeof(Array)],
            typeof(ForkBodyWriter).Module,
            true);

        ILGenerator il = call.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, bytes);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, entries);
        il.Emit(OpCodes.Call, map);
        il.Emit(OpCodes.Call, replace);
        il.Emit(OpCodes.Ret);
        return call.CreateDelegate<Action<MethodBase, byte[], Array>>();
    }

    private static PlatformNotSupportedException Missing(string member) {
        return new PlatformNotSupportedException(
            $"'{RuntimeCapability.UpdaterTypeName}'.{member} did not bind on '{RuntimeCapability.Runtime}', so no " +
            "method body can be replaced here. The caller has to keep the detour backend.");
    }
}

/// <summary>
///     Composes a target's injections into one body and writes it, or hands the target to the fallback
///     backend when anything about it refuses replacement. A handed-off target stays with the fallback.
/// </summary>
public sealed class ComposedBodyPublisher : ITargetBodyOwner {
    private readonly ConcurrentDictionary<MethodBase, Handoff> handedOff = new ConcurrentDictionary<MethodBase, Handoff>();

    private readonly IDetourBackend fallback;

    private readonly ForkBodyWriter writer;

    /// <summary>Initializes a new instance falling back to the native detour backend.</summary>
    public ComposedBodyPublisher() : this(new MonoModDetourBackend(), ForkBodyWriter.Shared) {
    }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="fallback">The backend that takes targets this one cannot replace.</param>
    public ComposedBodyPublisher(IDetourBackend fallback) : this(fallback, ForkBodyWriter.Shared) {
    }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="fallback">The backend that takes targets this one cannot replace.</param>
    /// <param name="writer">The writer that installs bodies, normally <see cref="ForkBodyWriter.Shared" />.</param>
    public ComposedBodyPublisher(IDetourBackend fallback, ForkBodyWriter writer) {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(writer);
        this.fallback = fallback;
        this.writer = writer;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A target that cannot be replaced is conceded to the fallback instead of throwing, and an injection
    ///     the composer rejected is logged and dropped rather than failing the rest.
    /// </remarks>
    public void PublishComposed(MethodBase target, IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ordered);

        if (handedOff.ContainsKey(target)) {
            Redetour(target, ordered);
            return;
        }

        bool held = WaylithDetourBackend.Writers.HoldsClaim(target);
        ComposeResult composed = WrapperComposer.Compose(target, ordered);
        foreach (RejectedInjection rejection in composed.Rejected) {
            if (EvictionReport.Compose("[Concord.Waylith] injection-evicted", target, rejection) is string report) {
                WaylithDetourBackend.Log(report);
            }
        }

        if (composed.Body is null) {
            HandOff(
                target,
                TargetEligibility.ForRejectedBody(target, composed.BodyRejection ?? "the compose reported no reason"),
                ordered,
                held);
            return;
        }

        EncodedBody encoded;
        IReadOnlyList<OffsetPair> map;
        try {
            encoded = BodyEncoder.Encode(composed.Body, WaylithDetourBackend.Tokens(target.Module), new ComputedStack());
            map = WaylithDetourBackend.OffsetMap(target, composed.Body, encoded, ordered);
        } catch (Exception failure) when (failure is not OutOfMemoryException) {
            HandOff(target, TargetEligibility.ForRuntimeRefusal(target, failure, "encoding its body"), ordered, held);
            return;
        }

        if (WaylithDetourBackend.Contested(target, encoded.Bytes, ordered) is string contested) {
            WaylithDetourBackend.Log(contested);
            Withdraw(target, held);
            Concede(target, ordered);
            return;
        }

        try {
            FrameAttribution.Register(target, composed.Body, encoded, ordered);
            writer.Write(target, encoded.Bytes, map);
            WaylithDetourBackend.Writers.Published(target, encoded.Bytes.Length);
        } catch (Exception failure) when (failure is not OutOfMemoryException) {
            HandOff(target, TargetEligibility.ForRuntimeRefusal(target, failure, "writing its body"), ordered, held);
            return;
        }

        if (map.Count == 0) {
            WaylithDetourBackend.Log(
                WaylithDetourBackend.NoOffsetMapWarning(target, WaylithDetourBackend.DistinctOwners(ordered)));
        }
    }

    /// <inheritdoc />
    /// <remarks>For a handed-off target this disposes the fallback's detour instead of writing a body.</remarks>
    public void RevertToOriginal(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        if (handedOff.TryGetValue(target, out Handoff? entry)) {
            entry.Detach();
            return;
        }

        if (WaylithDetourBackend.Writers.HoldsClaim(target)) {
            writer.Restore(target);
        }

        FrameAttribution.Forget(target);
        WaylithDetourBackend.Writers.Release(target);
    }

    /// <summary>Reports whether <paramref name="target" /> has been conceded to the fallback backend.</summary>
    /// <param name="target">The method to check.</param>
    /// <returns><see langword="true" /> once conceded, which is permanent for the rest of the process.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public bool IsHandedOff(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return handedOff.ContainsKey(target);
    }

    /// <summary>Concedes <paramref name="target" /> to the fallback for good, before any detour is installed.</summary>
    /// <param name="target">The method to concede. Marking one twice is harmless.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public void MarkHandedOff(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        handedOff.TryAdd(target, new Handoff());
    }

    private void HandOff(MethodBase target, TargetVerdict verdict, IReadOnlyList<Injection> ordered, bool held) {
        if (TargetEligibility.ReportOnce(target)) {
            WaylithDetourBackend.Log(verdict.Message);
        }

        Withdraw(target, held);
        Concede(target, ordered);
    }

    private void Withdraw(MethodBase target, bool claimed) {
        if (!claimed || !writer.IsAvailable) {
            return;
        }

        try {
            writer.Restore(target);
        } catch (Exception failure) when (failure is not OutOfMemoryException) {
            WaylithDetourBackend.Log(
                WaylithTargetStack.UndoFailedMarker + " '" + WaylithDetourBackend.Describe(target) + "': " +
                failure.GetType().Name + ", " + failure.Message.TrimEnd('.') +
                ". Its composed body could not be taken back off, so the detour installed next runs over a " +
                "body that is no longer the target's original one.");
        }
    }

    private void Concede(MethodBase target, IReadOnlyList<Injection> ordered) {
        FrameAttribution.Forget(target);
        WaylithDetourBackend.Writers.Release(target);
        MarkHandedOff(target);
        Redetour(target, ordered);
    }

    private void Redetour(MethodBase target, IReadOnlyList<Injection> ordered) {
        Handoff entry = handedOff[target];
        entry.Detach();
        if (ordered.Count > 0) {
            entry.Attach(fallback.ApplyComposed(target, ordered));
        }
    }

    private sealed class Handoff {
        private IDetourHandle? handle;

        internal void Attach(IDetourHandle applied) {
            handle = applied;
        }

        internal void Detach() {
            IDetourHandle? previous = handle;
            handle = null;
            previous?.Dispose();
        }
    }
}

/// <summary>
///     The <see cref="IDetourBackend" /> a caller selects to get body replacement instead of native
///     detours. <see cref="Apply" /> and <see cref="ApplyComposed" /> are the two ways in.
/// </summary>
public sealed class WaylithDetourBackend : IDetourBackend, IOwnsBodyQuery, ISuppressionProbes {
    /// <summary>Log prefix for a target published without an offset map, so its line numbers are unreliable.</summary>
    public const string NoOffsetMapMarker = "[Concord.Waylith] no-offset-map";

    /// <summary>The highest original offset the runtime is known to carry; past it no pairing is safe.</summary>
    public const int SentinelCeiling = 100_000_000;

    private const string ProfilingSwitch = "CORECLR_ENABLE_PROFILING";

    private static readonly ConcurrentDictionary<MethodBase, byte> Forwarded = new ConcurrentDictionary<MethodBase, byte>();

    private static readonly ConcurrentDictionary<MethodBase, object> Gates = new ConcurrentDictionary<MethodBase, object>();

    private static readonly IReadOnlyList<string> NoOwners = [];

    private static readonly IReadOnlyList<Injection> NoInjections = [];

    private static bool emptyMapAlways;

    private readonly IDetourBackend fallback;

    private readonly ITargetBodyOwner publisher;

    private readonly ForkBodyWriter writer;

    private readonly WaylithTargetStack stack;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="fallback">The backend that takes every target this one refuses.</param>
    public WaylithDetourBackend(IDetourBackend fallback)
        : this(fallback, Publisher ?? new ComposedBodyPublisher(fallback), ForkBodyWriter.Shared) {
    }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="fallback">The backend that takes every target this one refuses.</param>
    /// <param name="publisher">The owner of each target's body.</param>
    /// <param name="writer">The writer used for forwarder bodies, normally <see cref="ForkBodyWriter.Shared" />.</param>
    public WaylithDetourBackend(IDetourBackend fallback, ITargetBodyOwner publisher, ForkBodyWriter writer) {
        ArgumentNullException.ThrowIfNull(fallback);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(writer);
        this.fallback = fallback;
        this.publisher = publisher;
        this.writer = writer;
        WrapperComposer.CaptureComposedBody = true;
        stack = new WaylithTargetStack(publisher, Log);
        ReversePatchFactory.OwnsBody = OwnsBody;
    }

    /// <summary>
    ///     The publisher every instance shares unless one is passed to the constructor. Setting it is for a
    ///     test that needs to watch what gets published.
    /// </summary>
    public static ITargetBodyOwner? Publisher { get; set; } = new ComposedBodyPublisher();

    /// <summary>The factory for a module's token source. Replace it to mint tokens some other way.</summary>
    public static Func<Module, ITokenSource> Tokens { get; set; } = static module => new WaylithTokenSource(module);

    /// <summary>Where this backend's reports go. It writes to <see cref="Trace" /> until a host redirects it.</summary>
    public static Action<string> Log { get; set; } = static message => Trace.WriteLine(message);

    /// <summary>The registry that decides who owns a target's body when two patchers want the same one.</summary>
    public static WriterRegistry Writers { get; set; } = WriterRegistry.Shared;

    /// <summary>
    ///     When on, no offset map is written for any body, so stack traces carry raw offsets. The environment
    ///     can turn it on too, so reading <see langword="false" /> back is not guaranteed.
    /// </summary>
    public static bool EmptyMapAlways {
        get => emptyMapAlways || AdapterEntryPoint.EmptyMapRequested(Log);
        set => emptyMapAlways = value;
    }

    /// <summary>Claims <paramref name="target" />'s body for these injections' owners, or reports the clash.</summary>
    /// <param name="target">The method about to be written.</param>
    /// <param name="body">The encoded body, used to tell a republish of the same bytes from a real clash.</param>
    /// <param name="ordered">The injections whose owners are claiming the target.</param>
    /// <returns><see langword="null" /> when the claim held; otherwise the message to log before conceding.</returns>
    /// <exception cref="ArgumentNullException">Either reference argument is <see langword="null" />.</exception>
    public static string? Contested(MethodBase target, ReadOnlySpan<byte> body, IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(ordered);

        string[] owners = new string[ordered.Count];
        for (int i = 0; i < ordered.Count; i++) {
            owners[i] = ordered[i].Owner;
        }

        return Contested(target, body, (IReadOnlyList<string>)owners);
    }

    /// <summary>Claims <paramref name="target" />'s body for <paramref name="owners" />, or reports the clash.</summary>
    /// <param name="target">The method about to be written.</param>
    /// <param name="body">The encoded body, used to tell a republish of the same bytes from a real clash.</param>
    /// <param name="owners">The mods claiming the target, empty for a forwarder that has no owner.</param>
    /// <returns><see langword="null" /> when the claim held; otherwise the message to log before conceding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public static string? Contested(MethodBase target, ReadOnlySpan<byte> body, IReadOnlyList<string> owners) {
        ArgumentNullException.ThrowIfNull(target);

        ForeignVerdict foreign = ForeignPatcherPolicy.Judge(target);
        if (foreign.Handling == ForeignHandling.Disown) {
            Writers.Release(target);
            return foreign.Message;
        }

        ContentionReport report = Writers.Claim(target, body, owners, foreign.Owners);
        return report.Finding == ContentionFinding.ConcordCollision ? report.ToString() : null;
    }

    /// <summary>Pairs each instruction's offset in the written body with an offset in the target's own IL.</summary>
    /// <param name="target">The method the body was composed for.</param>
    /// <param name="body">The composed body, whose instructions carry their origin offsets.</param>
    /// <param name="encoded">The same body encoded, whose offsets must line up one for one.</param>
    /// <param name="ordered">The injections in the body, used only to decide whether a map is written.</param>
    /// <returns>The map, or an empty list when no pairing is safe and raw offsets get reported instead.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The composed and encoded bodies disagree on instruction count.</exception>
    public static IReadOnlyList<OffsetPair> OffsetMap(
        MethodBase target,
        ComposedBody body,
        EncodedBody encoded,
        IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(ordered);

        if (body.Instructions.Count != encoded.InstructionOffsets.Count) {
            throw new ArgumentException(
                $"The composed body holds {body.Instructions.Count} instructions and the encoded body reports " +
                $"{encoded.InstructionOffsets.Count} offsets, so the two cannot be paired into an offset map.",
                nameof(encoded));
        }

        int band = Band(target, encoded, ordered);
        if (band < 0) {
            return [];
        }

        List<OffsetPair> pairs = new List<OffsetPair>(body.Instructions.Count);
        for (int i = 0; i < body.Instructions.Count; i++) {
            int origin = body.Instructions[i].OriginOffset ?? (band + encoded.InstructionOffsets[i]);
            if (pairs.Count > 0 && pairs[^1].Original == origin) {
                continue;
            }

            pairs.Add(new OffsetPair(origin, encoded.InstructionOffsets[i]));
        }

        return pairs;
    }

    /// <summary>The offset a composed instruction with no origin is mapped to, laid after the target's own IL.</summary>
    /// <param name="target">The method whose original IL length sets the band.</param>
    /// <param name="encoded">The encoded body, whose size has to fit under the ceiling.</param>
    /// <returns><c>-1</c> when the target has no readable body or the band would pass <see cref="SentinelCeiling" />.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    public static int SentinelBand(MethodBase target, EncodedBody encoded) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(encoded);

        int original = target.GetMethodBody()?.GetILAsByteArray()?.Length ?? -1;
        return original < 0 || (long)original + encoded.CodeSize > SentinelCeiling ? -1 : original;
    }

    /// <summary>Counts the mods behind a set of injections, for a message that reports attribution.</summary>
    /// <param name="ordered">The injections to count owners across.</param>
    /// <returns>The number of distinct owner names, compared byte for byte.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ordered" /> is <see langword="null" />.</exception>
    public static int DistinctOwners(IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(ordered);

        HashSet<string> owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (Injection injection in ordered) {
            owners.Add(injection.Owner);
        }

        return owners.Count;
    }

    /// <summary>Builds the warning logged when a target is published with no offset map.</summary>
    /// <param name="target">The method that was published.</param>
    /// <param name="owners">How many mods own injections in it; <c>0</c> drops the attribution sentence.</param>
    /// <returns>A message starting with <see cref="NoOffsetMapMarker" />, naming the cause and the cost.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <see langword="null" />.</exception>
    public static string NoOffsetMapWarning(MethodBase target, int owners) {
        ArgumentNullException.ThrowIfNull(target);

        string cause = EmptyMapAlways
            ? $"{AdapterEntryPoint.EmptyMapVariable} is on, so no map is written for any body, whatever " +
                "its owners. Turn it off to get sentinel maps back"
            : "the body written for it, laid end to end after the target's own il, runs past the offset the " +
                $"runtime is known to carry ({SentinelCeiling}), so no pairing was safe";

        string attribution = owners > 0
            ? " The mod named as the owner of a frame in it stays correct."
            : string.Empty;

        return NoOffsetMapMarker + $" '{Describe(target)}': {cause}. The runtime reports raw offsets into the " +
            "written body instead, and a pdb resolves those against the original body, so any line number " +
            "reported for this method is unreliable: it names a real line that is not the code that ran." +
            attribution;
    }

    /// <summary>Builds the body that loads every argument, calls <paramref name="replacement" /> and returns.</summary>
    /// <param name="target">The method whose arguments the forwarder loads, receiver included when it has one.</param>
    /// <param name="replacement">The static method the forwarder calls.</param>
    /// <returns>A composed body with no exception handlers and no locals.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">The target is an instance method with no declaring type.</exception>
    public static ComposedBody ForwarderBody(MethodBase target, MethodInfo replacement) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replacement);

        Type[] arguments = ForwardedArguments(target);
        ComposedInstruction[] stream = new ComposedInstruction[arguments.Length + 2];
        for (int i = 0; i < arguments.Length; i++) {
            stream[i] = new ComposedInstruction(new CodeInstruction(OpCodes.Ldarg, i), null, null);
        }

        stream[arguments.Length] = new ComposedInstruction(new CodeInstruction(OpCodes.Call, replacement), null, null);
        stream[arguments.Length + 1] = new ComposedInstruction(new CodeInstruction(OpCodes.Ret), null, null);
        return new ComposedBody(stream, [], [], false);
    }

    /// <summary>Checks the pair a caller got wrong: return type, staticness and argument list.</summary>
    /// <param name="target">The method to be forwarded.</param>
    /// <param name="replacement">The method it would be forwarded to.</param>
    /// <returns><see langword="null" /> when the pair matches; otherwise the reason, which callers throw.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    public static string? ForwarderRefusal(MethodBase target, MethodInfo replacement) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replacement);

        string named = $"'{Describe(target)}' cannot be forwarded to '{Describe(replacement)}' by replacing its body";
        Type returned = target is MethodInfo method ? method.ReturnType : typeof(void);
        if (replacement.ReturnType != returned) {
            return named + $": the target returns '{returned}' and the replacement returns " +
                $"'{replacement.ReturnType}', and a forwarder has nowhere to convert between them.";
        }

        if (!replacement.IsStatic) {
            return named + ": the replacement is an instance method, so the forwarder has no receiver to load for " +
                "it. Make the replacement static and let its first parameter take the target's receiver.";
        }

        Type[] expected = ForwardedArguments(target);
        ParameterInfo[] supplied = replacement.GetParameters();
        if (supplied.Length != expected.Length) {
            return named + $": the forwarder loads {expected.Length} argument(s) and the replacement takes " +
                $"{supplied.Length}. A replaced body can neither drop an argument nor invent one.";
        }

        for (int i = 0; i < expected.Length; i++) {
            if (supplied[i].ParameterType != expected[i]) {
                return named + $": argument {i} is '{expected[i]}' on the target and " +
                    $"'{supplied[i].ParameterType}' on the replacement.";
            }
        }

        return null;
    }

    /// <summary>Checks the shapes no forwarder can express: dynamic replacement, generic target, vararg.</summary>
    /// <param name="target">The method to be forwarded.</param>
    /// <param name="replacement">The method it would be forwarded to.</param>
    /// <returns><see langword="null" /> when the shape works; otherwise the reason to log before falling back.</returns>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null" />.</exception>
    public static string? UnforwardableShape(MethodBase target, MethodInfo replacement) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(replacement);

        string named = $"'{Describe(target)}' cannot be forwarded to '{Describe(replacement)}' by replacing its body";
        if (replacement.Module.Assembly.IsDynamic) {
            return named + ": the replacement lives in a dynamic assembly, and a dynamic method has no metadata " +
                "token for a replaced body to call.";
        }

        if (target.ContainsGenericParameters || target.IsGenericMethod || target.DeclaringType?.IsGenericType == true) {
            return named + ": the target is generic, and one replaced body serves every instantiation of the " +
                "typical definition, so a single forwarder cannot name the right instantiation of the replacement.";
        }

        if (IsVarArg(target) || IsVarArg(replacement)) {
            return named + ": a vararg call site is a standalone signature rather than a member, and the runtime's " +
                "token api mints members only.";
        }

        return null;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     True for a target this backend published a body over, and for one the fallback owns, because a
    ///     refused target is detoured through that fallback and is Concord-owned just the same.
    /// </remarks>
    public bool OwnsBody(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Writers.HoldsClaim(target) || (fallback as IOwnsBodyQuery)?.OwnsBody(target) == true;
    }

    /// <inheritdoc />
    public string? BodyReplacementUnavailable() {
        return RuntimeCapability.IsAvailable ? null : RuntimeCapability.Explain();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Reads the variable a CoreCLR profiler is attached through at startup. A profiler attached later
    ///     is not visible here, and there is no runtime api that reports one.
    /// </remarks>
    public bool ProfilerMayRewriteBodies() {
        return Environment.GetEnvironmentVariable(ProfilingSwitch) == "1";
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Writes a forwarder body rather than a detour, so the replacement must be static and match the
    ///     target's arguments exactly. A target this cannot forward falls back instead of throwing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The target already carries a body installed here.</exception>
    /// <exception cref="NotSupportedException">The replacement's shape cannot be forwarded to at all.</exception>
    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);

        MethodBase target = MethodIdentity.Normalize(original);
        MethodBase key = BodyScope.KeyOf(target);
        IDetourHandle? forwarded;
        string reason;
        lock (Gate(key)) {
            forwarded = Forward(target, key, replacement, out reason);
        }

        return forwarded ?? FallBack(target, reason, original, replacement);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     An ineligible target is passed to the fallback and stays there. A generic target is keyed by body
    ///     scope, so one body serves every instantiation.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The target already carries a forwarder from <see cref="Apply" />.</exception>
    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(added);

        MethodBase key = BodyScope.KeyOf(target);
        lock (Gate(key)) {
            if (Forwarded.ContainsKey(key)) {
                throw new InvalidOperationException(
                    $"'{Describe(key)}' already carries a forwarder body installed through Apply, and a method has " +
                    "exactly one body. Composing injections over it would discard the forwarder silently.");
            }

            TargetVerdict verdict = TargetEligibility.Judge(key);
            verdict.ThrowIfRefused();
            if (verdict.Handling == TargetHandling.Replace) {
                verdict = SharedGenericPolicy.Judge(target);
                verdict.ThrowIfRefused();
            }

            if (verdict.Handling != TargetHandling.Detour) {
                return stack.Add(target, added);
            }

            if (TargetEligibility.ReportOnce(key)) {
                Log(verdict.Message);
            }

            if (publisher is ComposedBodyPublisher owner) {
                owner.MarkHandedOff(key);
            }
        }

        return fallback.ApplyComposed(target, added);
    }

    internal static int Band(MethodBase target, EncodedBody encoded, IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(ordered);

        return EmptyMapAlways ? -1 : SentinelBand(target, encoded);
    }

    internal static object Gate(MethodBase key) {
        return Gates.GetOrAdd(key, static _ => new object());
    }

    internal static string Describe(MethodBase method) {
        return (method.DeclaringType?.FullName ?? method.Module.Name) + "." + method.Name;
    }

    private static Type[] ForwardedArguments(MethodBase target) {
        ParameterInfo[] parameters = target.GetParameters();
        if (target.IsStatic) {
            Type[] statics = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) {
                statics[i] = parameters[i].ParameterType;
            }

            return statics;
        }

        Type declaring = target.DeclaringType ?? throw new NotSupportedException(
            $"'{Describe(target)}' is an instance method with no declaring type, so its receiver has no type to forward.");

        Type[] arguments = new Type[parameters.Length + 1];
        arguments[0] = declaring.IsValueType ? declaring.MakeByRefType() : declaring;
        for (int i = 0; i < parameters.Length; i++) {
            arguments[i + 1] = parameters[i].ParameterType;
        }

        return arguments;
    }

    private static InvalidOperationException AlreadyForwarded(MethodBase target) {
        return new InvalidOperationException(
            $"'{Describe(target)}' already carries a forwarder body installed through {nameof(Apply)}, and a " +
            "method has exactly one body. Writing a second forwarder over it would discard the first while the " +
            "handle that owns it still reports IsApplied. Dispose that handle before forwarding this target again.");
    }

    private static bool IsVarArg(MethodBase method) {
        return (method.CallingConvention & CallingConventions.VarArgs) != 0;
    }

    private IDetourHandle? Forward(MethodBase target, MethodBase key, MethodInfo replacement, out string reason) {
        reason = string.Empty;
        if (stack.IsPublished(target)) {
            throw new InvalidOperationException(
                $"'{Describe(target)}' already carries a composed body installed through {nameof(ApplyComposed)}, " +
                "and a method has exactly one body. Writing a forwarder over it would discard those injections " +
                "while the handles that own them still report IsApplied.");
        }

        if (Forwarded.ContainsKey(key)) {
            throw AlreadyForwarded(target);
        }

        TargetVerdict verdict = TargetEligibility.Judge(target);
        verdict.ThrowIfRefused();
        if (verdict.Handling == TargetHandling.Detour) {
            reason = verdict.Message;
            return null;
        }

        if (ForwarderRefusal(target, replacement) is string refusal) {
            throw new NotSupportedException(refusal);
        }

        if (UnforwardableShape(target, replacement) is string shape) {
            reason = TargetEligibility.FallbackMarker + " " + shape;
            return null;
        }

        if (!Forwarded.TryAdd(key, 0)) {
            throw AlreadyForwarded(target);
        }

        bool forwarded = false;
        try {
            IReadOnlyList<OffsetPair> map;
            try {
                ComposedBody forwarder = ForwarderBody(target, replacement);
                EncodedBody encoded = BodyEncoder.Encode(forwarder, Tokens(target.Module), new ComputedStack());
                if (Contested(target, encoded.Bytes, NoOwners) is string contested) {
                    reason = contested;
                    return null;
                }

                map = OffsetMap(target, forwarder, encoded, NoInjections);
                writer.Write(target, encoded.Bytes, map);
                Writers.Published(target, encoded.Bytes.Length);
            } catch (Exception failure) when (failure is not OutOfMemoryException) {
                Writers.Release(target);
                reason = TargetEligibility.ForRuntimeRefusal(target, failure, "publishing its forwarder body").Message;
                return null;
            }

            if (map.Count == 0) {
                Log(NoOffsetMapWarning(target, 0));
            }

            forwarded = true;
            return new ForwarderHandle(this, target, key);
        } finally {
            if (!forwarded) {
                Forwarded.TryRemove(key, out _);
            }
        }
    }

    private IDetourHandle FallBack(MethodBase target, string reason, MethodBase original, MethodInfo replacement) {
        if (TargetEligibility.ReportOnce(target)) {
            Log(reason);
        }

        if (publisher is ComposedBodyPublisher owner) {
            owner.MarkHandedOff(target);
        }

        return fallback.Apply(original, replacement);
    }

    private sealed class ForwarderHandle : IDetourHandle {
        private readonly WaylithDetourBackend owner;

        private readonly MethodBase key;

        private int removed;

        internal ForwarderHandle(WaylithDetourBackend owner, MethodBase original, MethodBase key) {
            this.owner = owner;
            this.key = key;
            Original = original;
        }

        public MethodBase Original { get; }

        public bool IsApplied => Volatile.Read(ref removed) == 0 && Forwarded.ContainsKey(key);

        public void Dispose() {
            if (Interlocked.Exchange(ref removed, 1) != 0 || Environment.HasShutdownStarted) {
                return;
            }

            try {
                lock (Gate(key)) {
                    owner.writer.Restore(Original);
                    Forwarded.TryRemove(key, out _);
                    Writers.Release(Original);
                }
            } catch (Exception failure) {
                Volatile.Write(ref removed, 0);
                Log($"{WaylithTargetStack.UndoFailedMarker} '{Describe(Original)}': {failure.GetType().Name}, " +
                    $"{failure.Message.TrimEnd('.')}. Its forwarder body keeps running.");
            }
        }
    }
}
