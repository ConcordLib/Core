using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Concord.Host.Adapter;

/// <summary>What an inspection saw about who else writes a target's body. A method has one body, so two writers mean the later one wins and the earlier patch silently stops running.</summary>
public enum ContentionFinding {
    /// <summary>Nothing contended was seen. Read it as "no contention observed", not as "Concord is the only writer".</summary>
    Unknown,

    /// <summary>Another Concord writer in this process claimed the target first, so the two overwrite each other.</summary>
    ConcordCollision,

    /// <summary>A foreign patcher owns the target, and its detour runs in front of the replaced body.</summary>
    ForeignOwner,
}

/// <summary>One target's last published body as this registry saw it: who asked for it, how big it was, when it went in, and how many times it has been replaced.</summary>
public sealed class WriterRecord {
    internal WriterRecord(
        MethodBase target,
        IReadOnlyList<string> owners,
        ulong fingerprint,
        int bodyLength,
        long sequence,
        DateTime publishedUtc,
        int publishCount,
        string writerId) {
        Target = target;
        Owners = owners;
        Fingerprint = fingerprint;
        BodyLength = bodyLength;
        Sequence = sequence;
        PublishedUtc = publishedUtc;
        PublishCount = publishCount;
        WriterId = writerId;
    }

    /// <summary>The patched method, as the caller handed it over rather than as the normalized key the registry files it under.</summary>
    public MethodBase Target { get; }

    /// <summary>The owner ids whose injections went into this body, in the order they run; empty when the body carries none.</summary>
    public IReadOnlyList<string> Owners { get; }

    /// <summary>An FNV-1a hash of the published IL, for telling two bodies apart in a log without printing either.</summary>
    public ulong Fingerprint { get; }

    /// <summary>The published body's length in bytes, counting the method header.</summary>
    public int BodyLength { get; }

    /// <summary>This registry's write counter at the moment of the publish, which orders claims against each other.</summary>
    public long Sequence { get; }

    /// <summary>When the body was published, in UTC.</summary>
    public DateTime PublishedUtc { get; }

    /// <summary>How many bodies this registry has published over this target, counting from one.</summary>
    public int PublishCount { get; }

    /// <summary>The id of the registry that published the body, which identifies one loaded copy of the adapter inside the process.</summary>
    public string WriterId { get; }

    /// <summary>Renders the record as one log line: target, body fingerprint and length, owners, publish count, timestamp and writer id.</summary>
    /// <returns>The log line.</returns>
    public override string ToString() {
        return WriterRegistry.Describe(Target) + ": body 0x" + Fingerprint.ToString("x16", CultureInfo.InvariantCulture) +
            ", " + BodyLength + " byte(s), owners [" + string.Join(", ", Owners) + "], publish #" + PublishCount +
            " (write " + Sequence + ") at " + PublishedUtc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
            "Z by writer " + WriterId;
    }
}

/// <summary>A claim or inspection result for one target, saying whether Concord's body is still the code that runs there and carrying the line to log about it.</summary>
public sealed class ContentionReport {
    internal ContentionReport(
        MethodBase target,
        WriterRecord? concord,
        ContentionFinding finding,
        bool poisoned,
        string explanation) {
        Target = target;
        Concord = concord;
        Finding = finding;
        Poisoned = poisoned;
        Explanation = explanation;
    }

    /// <summary>The method this report is about, as the key the registry files claims under.</summary>
    public MethodBase Target { get; }

    /// <summary>Concord's own record for the target, or <c>null</c> when this registry holds no claim on it.</summary>
    public WriterRecord? Concord { get; }

    /// <summary>Which contending writer, if any, the inspection could see. <see cref="ContentionFinding.Unknown" /> with <see cref="Poisoned" /> false is the clean case.</summary>
    public ContentionFinding Finding { get; }

    /// <summary>Whether a foreign patcher has ever owned this target in this process. Once that happens, body replacement on the method stays dead for the rest of the process and unpatching does not repair it.</summary>
    public bool Poisoned { get; }

    /// <summary>The reasoning in full, ready to log, including what Concord cannot see about the target.</summary>
    public string Explanation { get; }

    /// <summary>The log marker matching the finding, falling back to <see cref="WriterRegistry.ContestedMarker" /> when the target is poisoned with no owner reported.</summary>
    public string Marker => Finding switch {
        ContentionFinding.ConcordCollision => WriterRegistry.CollisionMarker,
        ContentionFinding.ForeignOwner => WriterRegistry.ContestedMarker,
        _ => Poisoned ? WriterRegistry.ContestedMarker : WriterRegistry.UnobservedMarker,
    };

    /// <summary>The exact one-line form the registry logs, so a reader can grep a log for the marker and find every contended target.</summary>
    /// <returns>The log line.</returns>
    public override string ToString() {
        return Marker + " '" + WriterRegistry.Describe(Target) + "': " + Explanation;
    }
}

/// <summary>Tracks which targets this process has published bodies over, and warns when a second writer takes one, since a method has one body and the last write wins.</summary>
public sealed class WriterRegistry {
    /// <summary>The log marker for two Concord writers landing on the same target.</summary>
    public const string CollisionMarker = "[Concord.Waylith] writer-collision";

    /// <summary>The log marker for a target a foreign patcher owns or has owned.</summary>
    public const string ContestedMarker = "[Concord.Waylith] writer-contested";

    /// <summary>The log marker for a target where nothing contended was seen, which is weaker than "nobody else wrote it".</summary>
    public const string UnobservedMarker = "[Concord.Waylith] writer-unobserved";

    /// <summary>The log marker on the whole-registry line that <see cref="Summary" /> builds.</summary>
    public const string SummaryMarker = "[Concord.Waylith] writer-registry";

    /// <summary>The caveat appended to any clean report: what this registry structurally cannot see.</summary>
    public const string BlindSpot =
        "Concord cannot read a method's active body back, so a foreign body replacement is never visible here. " +
        "An unobserved target means no contention was seen, not that Concord is the sole writer.";

    private const string ClaimSlot = "Concord.Host.Adapter.WriterRegistry.Claims";

    private const ulong FnvOffset = 14695981039346656037UL;

    private const ulong FnvPrime = 1099511628211UL;

    private static readonly object SlotGate = AppDomain.CurrentDomain;

    private static readonly string[] NoOwners = [];

    private readonly ConcurrentDictionary<MethodBase, WriterRecord> claims = new ConcurrentDictionary<MethodBase, WriterRecord>();

    private readonly ConcurrentDictionary<MethodBase, ContentionFinding> reported = new ConcurrentDictionary<MethodBase, ContentionFinding>();

    private readonly ConcurrentDictionary<MethodBase, string> processClaims;

    private readonly Action<string> log;

    private long writes;

    private long bytes;

    private long sequences;

    /// <summary>Initializes a new instance sharing the process-wide claim table, so a second loaded copy of the adapter is detected as a collision.</summary>
    /// <param name="log">Where contention lines go; called once per target per distinct finding.</param>
    /// <exception cref="ArgumentNullException"><paramref name="log" /> is <c>null</c>.</exception>
    public WriterRegistry(Action<string> log) : this(log, ProcessClaims()) {
    }

    /// <summary>Initializes a new instance over a caller-supplied claim table, which isolates it from the process-wide one for tests.</summary>
    /// <param name="log">Where contention lines go; called once per target per distinct finding.</param>
    /// <param name="processClaims">The table mapping a target to the writer id that holds it.</param>
    /// <exception cref="ArgumentNullException">Either argument is <c>null</c>.</exception>
    public WriterRegistry(Action<string> log, ConcurrentDictionary<MethodBase, string> processClaims) {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(processClaims);
        this.log = log;
        this.processClaims = processClaims;
        WriterId = WriterIdentity(GetType().Assembly);
    }

    /// <summary>The registry the backend itself claims through, logging to the backend's log. Use this one unless a test needs an isolated registry.</summary>
    public static WriterRegistry Shared { get; } = new WriterRegistry(static message => WaylithDetourBackend.Log(message));

    /// <summary>The id this registry stamps on its claims; it is unique per instance, so two loaded copies of the adapter never look like one writer.</summary>
    public string WriterId { get; }

    /// <summary>How many bodies this registry has published, counting every republish of the same target.</summary>
    public long PublishedBodies => Interlocked.Read(ref writes);

    /// <summary>Total bytes of replaced IL this registry has published. The runtime never frees a replaced body, so this number only grows.</summary>
    public long PublishedBytes => Interlocked.Read(ref bytes);

    /// <summary>How many targets this registry currently holds a claim on.</summary>
    public int ClaimCount => claims.Count;

    /// <summary>FNV-1a hash of an encoded body, so two publishes can be compared in a log without printing either body.</summary>
    /// <param name="body">The encoded body bytes.</param>
    /// <returns>The hash. An empty span hashes to the FNV-1a offset basis, not to zero.</returns>
    public static ulong Fingerprint(ReadOnlySpan<byte> body) {
        ulong hash = FnvOffset;
        foreach (byte value in body) {
            hash = unchecked((hash ^ value) * FnvPrime);
        }

        return hash;
    }

    /// <summary>Names a method for a log line. Parameters are left out, so two overloads describe identically.</summary>
    /// <param name="method">The method to name.</param>
    /// <returns>The declaring type's full name and the method name, or the module name and the method name when there is no declaring type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="method" /> is <c>null</c>.</exception>
    public static string Describe(MethodBase method) {
        ArgumentNullException.ThrowIfNull(method);
        return (method.DeclaringType?.FullName ?? method.Module.Name) + "." + method.Name;
    }

    /// <summary>Adds a published body to the <see cref="PublishedBodies" /> and <see cref="PublishedBytes" /> counters. Call it after the runtime accepts the body, not before.</summary>
    /// <param name="target">The method whose body was published.</param>
    /// <param name="length">The published body's length in bytes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public void Published(MethodBase target, int length) {
        ArgumentNullException.ThrowIfNull(target);
        Interlocked.Increment(ref writes);
        Interlocked.Add(ref bytes, length);
    }

    /// <summary>Records a publish, taking the owner list from the injections that went into the body in run order.</summary>
    /// <param name="target">The method whose body was published.</param>
    /// <param name="body">The encoded body, read for its length and fingerprint only.</param>
    /// <param name="ordered">The injections composed into the body, in the order they run.</param>
    /// <returns>The report for this target, already logged if it found contention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> or <paramref name="ordered" /> is <c>null</c>.</exception>
    public ContentionReport Claim(MethodBase target, ReadOnlySpan<byte> body, IReadOnlyList<Injection> ordered) {
        ArgumentNullException.ThrowIfNull(ordered);

        string[] owners = new string[ordered.Count];
        for (int i = 0; i < ordered.Count; i++) {
            owners[i] = ordered[i].Owner;
        }

        return Claim(target, body, owners);
    }

    /// <summary>Records a publish and probes the foreign patchers Concord knows how to question for owners of this target.</summary>
    /// <param name="target">The method whose body was published.</param>
    /// <param name="body">The encoded body, read for its length and fingerprint only.</param>
    /// <param name="owners">The owner ids behind the body, in run order.</param>
    /// <returns>The report for this target, already logged if it found contention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> or <paramref name="owners" /> is <c>null</c>.</exception>
    public ContentionReport Claim(MethodBase target, ReadOnlySpan<byte> body, IReadOnlyList<string> owners) {
        ArgumentNullException.ThrowIfNull(target);
        return Claim(target, body, owners, ForeignPatcherPolicy.HarmonyOwners(target));
    }

    /// <summary>Records a publish against a caller-supplied set of foreign owners instead of probing for them. If another writer already holds the target, nothing is recorded and the collision is reported.</summary>
    /// <param name="target">The method whose body was published.</param>
    /// <param name="body">The encoded body, read for its length and fingerprint only.</param>
    /// <param name="owners">The owner ids behind the body, in run order.</param>
    /// <param name="foreign">Foreign patcher owners of this target, or <c>null</c> when none were found.</param>
    /// <returns>The report for this target, already logged if it found contention.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> or <paramref name="owners" /> is <c>null</c>.</exception>
    public ContentionReport Claim(
        MethodBase target,
        ReadOnlySpan<byte> body,
        IReadOnlyList<string> owners,
        IReadOnlyList<string>? foreign) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(owners);

        MethodBase key = Key(target);
        string holder = processClaims.GetOrAdd(key, WriterId);
        if (holder != WriterId) {
            claims.TryGetValue(key, out WriterRecord? held);
            return Announced(Judge(key, held, holder, foreign));
        }

        int length = body.Length;
        long sequence = Interlocked.Increment(ref sequences);
        ulong fingerprint = Fingerprint(body);
        string[] copy = Copy(owners);

        WriterRecord record = claims.AddOrUpdate(
            key,
            _ => new WriterRecord(target, copy, fingerprint, length, sequence, DateTime.UtcNow, 1, WriterId),
            (_, previous) => new WriterRecord(
                target, copy, fingerprint, length, sequence, DateTime.UtcNow, previous.PublishCount + 1, WriterId));

        return Announced(Judge(key, record, holder, foreign));
    }

    /// <summary>Drops this registry's claim on a target and forgets what was reported about it. The published body stays in place; releasing a claim does not restore the original method.</summary>
    /// <param name="target">The method to release.</param>
    /// <returns><c>true</c> when a record was removed, <c>false</c> when this registry held no claim.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public bool Release(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);

        MethodBase key = Key(target);
        processClaims.TryRemove(new KeyValuePair<MethodBase, string>(key, WriterId));
        reported.TryRemove(key, out _);
        ForeignPatcherPolicy.Forget(target);
        return claims.TryRemove(key, out _);
    }

    /// <summary>Whether this registry has a record for the target. Normalization applies, so any reflection path to the same method answers the same.</summary>
    /// <param name="target">The method to look up.</param>
    /// <returns><c>true</c> when this registry holds the claim, <c>false</c> when it does not, including when another writer holds it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public bool HoldsClaim(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return claims.ContainsKey(Key(target));
    }

    /// <summary>The last body this registry published over a target, for a diagnostic that wants the fingerprint, size or publish count.</summary>
    /// <param name="target">The method to look up.</param>
    /// <returns>The record, or <c>null</c> when this registry holds no claim on the target.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public WriterRecord? RecordFor(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return claims.TryGetValue(Key(target), out WriterRecord? record) ? record : null;
    }

    /// <summary>Re-judges one target now, probing the foreign patchers Concord can question. Nothing is logged; use it for an on-demand diagnostic.</summary>
    /// <param name="target">The method to inspect, claimed or not.</param>
    /// <returns>The report, with <see cref="ContentionFinding.Unknown" /> and <see cref="ContentionReport.Poisoned" /> false when nothing contended was seen.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target" /> is <c>null</c>.</exception>
    public ContentionReport Inspect(MethodBase target) {
        ArgumentNullException.ThrowIfNull(target);
        return Inspect(target, ForeignPatcherPolicy.Probe());
    }

    /// <summary>Every claim this registry holds, oldest publish first, as a snapshot that later publishes do not change.</summary>
    /// <returns>The records, ordered by write sequence; empty when nothing is claimed.</returns>
    public IReadOnlyList<WriterRecord> Claims() {
        List<WriterRecord> snapshot = new List<WriterRecord>(claims.Count);
        foreach (KeyValuePair<MethodBase, WriterRecord> entry in claims) {
            snapshot.Add(entry.Value);
        }

        snapshot.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
        return snapshot;
    }

    /// <summary>Re-judges every claimed target in one pass and logs anything newly contended, for a sweep after all patching is done.</summary>
    /// <returns>Only the reports that found contention or a poisoned target; empty when every claim is clean.</returns>
    public IReadOnlyList<ContentionReport> Checkpoint() {
        List<ContentionReport> found = [];
        ForeignPatcherPolicy.HarmonyProbe probe = ForeignPatcherPolicy.Probe();
        foreach (KeyValuePair<MethodBase, WriterRecord> entry in claims) {
            ContentionReport report = Inspect(entry.Value.Target, probe);
            if (report.Finding == ContentionFinding.Unknown && !report.Poisoned) {
                continue;
            }

            found.Add(Announced(report));
        }

        return found;
    }

    /// <summary>One line totalling claims, published bodies, bytes of IL that will never be freed, and poisoned targets, for a shutdown or status dump.</summary>
    /// <returns>The line, prefixed with <see cref="SummaryMarker" /> and ending with <see cref="BlindSpot" />.</returns>
    public string Summary() {
        int stuck = 0;
        ForeignPatcherPolicy.HarmonyProbe probe = ForeignPatcherPolicy.Probe();
        foreach (WriterRecord record in claims.Values) {
            if (Contested(record.Target, probe.Owners(record.Target))) {
                stuck++;
            }
        }

        return SummaryMarker + ": " + claims.Count + " target(s) claimed, " + PublishedBodies + " body/bodies published, " +
            PublishedBytes + " byte(s) of replaced IL allocated and never freed, " + stuck +
            " target(s) permanently poisoned by a foreign patcher. " + BlindSpot;
    }

    internal static ConcurrentDictionary<MethodBase, string> ProcessClaims() {
        lock (SlotGate) {
            if (AppDomain.CurrentDomain.GetData(ClaimSlot) is ConcurrentDictionary<MethodBase, string> existing) {
                return existing;
            }

            AppDomain.CurrentDomain.SetData(ClaimSlot, new ConcurrentDictionary<MethodBase, string>());
            return (ConcurrentDictionary<MethodBase, string>)AppDomain.CurrentDomain.GetData(ClaimSlot)!;
        }
    }

    private static string WriterIdentity(Assembly assembly) {
        string name = assembly.IsDynamic || assembly.Location.Length == 0
            ? assembly.FullName ?? assembly.GetName().Name ?? "unknown"
            : assembly.Location;

        return name + "#" + Guid.NewGuid().ToString("N")[..8];
    }

    private static MethodBase Key(MethodBase target) {
        return BodyScope.KeyOf(target);
    }

    private static string[] Copy(IReadOnlyList<string> owners) {
        if (owners.Count == 0) {
            return NoOwners;
        }

        string[] copy = new string[owners.Count];
        for (int i = 0; i < owners.Count; i++) {
            copy[i] = owners[i];
        }

        return copy;
    }

    private static bool Contested(MethodBase target, IReadOnlyList<string>? probed) {
        return probed is not null || ForeignPatcherPolicy.IsDisowned(target);
    }

    private ContentionReport Inspect(MethodBase target, ForeignPatcherPolicy.HarmonyProbe probe) {
        MethodBase key = Key(target);
        claims.TryGetValue(key, out WriterRecord? record);
        processClaims.TryGetValue(key, out string? holder);
        return Judge(key, record, holder ?? WriterId, probe.Owners(target));
    }

    private ContentionReport Judge(MethodBase key, WriterRecord? record, string holder, IReadOnlyList<string>? probed) {
        IReadOnlyList<string> foreign = probed ?? NoOwners;
        bool stuck = Contested(key, probed);

        if (holder != WriterId) {
            return new ContentionReport(
                key,
                record,
                ContentionFinding.ConcordCollision,
                stuck,
                "Concord writer '" + holder + "' already claimed this target and this registry is '" + WriterId +
                "'. Two Concord writers in one process overwrite each other, because a method has exactly one body " +
                "and replacement is last-write-wins.");
        }

        if (stuck) {
            return new ContentionReport(
                key,
                record,
                foreign.Count > 0 ? ContentionFinding.ForeignOwner : ContentionFinding.Unknown,
                true,
                (foreign.Count > 0
                    ? "[" + string.Join(", ", foreign) + "] own this target through a foreign patcher, and a detour " +
                    "runs in front of a replaced body, so Concord's body is not the code that runs."
                    : "A foreign patcher has owned this target at some point in this process, even though none is " +
                    "reported now.") +
                " Harmony poisons a target permanently: after it has patched once, body replacement on that method " +
                "stays dead for the rest of the process, and unpatching does not repair it.");
        }

        return new ContentionReport(
            key,
            record,
            ContentionFinding.Unknown,
            false,
            (record is null ? "Concord holds no claim on this target. " : "Concord holds the claim on this target. ") +
            "No patcher Concord can question reports this target, and a patcher Concord cannot question reads the " +
            "same way. " + BlindSpot);
    }

    private ContentionReport Announced(ContentionReport report) {
        if (report.Finding == ContentionFinding.Unknown && !report.Poisoned) {
            return report;
        }

        if (reported.TryGetValue(report.Target, out ContentionFinding already) && already == report.Finding) {
            return report;
        }

        reported[report.Target] = report.Finding;
        log(report.ToString());
        return report;
    }
}
