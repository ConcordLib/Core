using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Entry = (int Slot, string Name, int ScopeStart, int ScopeEnd);
using Module = System.Reflection.Module;

namespace Concord.Emit;

/// <summary>
///     Reads local-variable source names out of a target method's pdb.
/// </summary>
/// <remarks>
///     Names live in debug symbols, not in metadata, so this only works where a pdb can be found
///     and matched. Everything it hands back is <see cref="int" /> and <see cref="string" />; no
///     Cecil type crosses the boundary, and no file handle outlives a call.
/// </remarks>
internal static class LocalNames {
    private static readonly IReadOnlyList<Entry> None = [];

    private static readonly object Gate = new object();

    private static readonly Dictionary<Guid, string> Unreadable = new Dictionary<Guid, string>();

    private static readonly Dictionary<(Guid Mvid, int Token), IReadOnlyList<Entry>> Cache =
        new Dictionary<(Guid, int), IReadOnlyList<Entry>>();

    private static Func<Module, string?>? pathResolver;

    private static int resolverVersion;

    /// <summary>
    ///     An adapter-supplied hook that maps a loaded module to the file its symbols live beside.
    ///     Returning a path skips the <see cref="Assembly.Location" /> probe entirely; returning
    ///     <see langword="null" /> falls back to it.
    /// </summary>
    internal static Func<Module, string?>? PathResolver {
        get => pathResolver;

        // An adapter that registers late would otherwise be stuck behind whatever the last probe
        // wrote. An MVID identifies the IL, not the pdb: Cecil matches a pdb by GUID plus age, so a
        // stripped one passes and yields fewer names, which means a positive entry can also be
        // stale. Both dictionaries go. The reference check keeps that free for the common case of
        // an adapter re-registering the same delegate on every mod load.
        set {
            lock (Gate) {
                if (ReferenceEquals(pathResolver, value)) {
                    return;
                }

                pathResolver = value;
                resolverVersion++;
                Unreadable.Clear();
                Cache.Clear();
            }
        }
    }

    /// <summary>
    ///     Every named local the symbols record for <paramref name="method" />, flattened across scopes.
    /// </summary>
    /// <param name="method">The method whose locals are being named.</param>
    internal static IReadOnlyList<Entry> For(MethodBase method) {
        return For(method, out _);
    }

    /// <summary>
    ///     Every named local the symbols record for <paramref name="method" />, flattened across scopes.
    ///     Empty when no symbols could be read, and then <paramref name="unreadable" /> says why.
    /// </summary>
    /// <param name="method">The method whose locals are being named.</param>
    /// <param name="unreadable">Why no symbols were read, naming the path searched, or null when they were.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S125", Justification = "Prose explaining why the hook runs outside Gate, not commented out code.")]
    internal static IReadOnlyList<Entry> For(MethodBase method, out string? unreadable) {
        // A DynamicMethod throws rather than returning a token, and its module is not on disk.
        int token = Token(method);
        if (token == 0) {
            unreadable = "the method was emitted at runtime and carries no metadata token";
            return None;
        }

        Module module = method.Module;
        Guid mvid = module.ModuleVersionId;

        while (true) {
            int version;
            lock (Gate) {
                if (Cached(mvid, token, out IReadOnlyList<Entry> hit, out unreadable)) {
                    return hit;
                }

                version = resolverVersion;
            }

            // The hook is foreign code, so it runs outside Gate: an adapter that takes its own lock
            // while holding ours inverts the order. Only File.Exists and the hook run out here;
            // Read stays inside, so a thread that loses the double-check returns the winner's
            // entries rather than opening the module a second time.
            string? path = Locate(module, out string missing);

            lock (Gate) {
                // A PathResolver set that landed while we were out of the lock just cleared both
                // dictionaries. Writing this probe's answer would re-stick the case that clear
                // exists to unstick, so throw it away and ask the new resolver instead.
                if (version != resolverVersion) {
                    continue;
                }

                if (Cached(mvid, token, out IReadOnlyList<Entry> hit, out unreadable)) {
                    return hit;
                }

                if (path is null) {
                    Unreadable[mvid] = missing;
                    unreadable = missing;
                    return None;
                }

                return Read(path, mvid, token, method, out unreadable);
            }
        }
    }

    private static int Token(MethodBase method) {
        try {
            return method.MetadataToken;
        } catch (InvalidOperationException) {
            return 0;
        }
    }

    // A module that failed once stays failed: loaded bytes cannot change, so a second probe would
    // only pay the file access again.
    private static bool Cached(Guid mvid, int token, out IReadOnlyList<Entry> entries, out string? unreadable) {
        lock (Gate) {
            if (Cache.TryGetValue((mvid, token), out IReadOnlyList<Entry>? hit)) {
                entries = hit;
                unreadable = null;
                return true;
            }

            if (Unreadable.TryGetValue(mvid, out string? reason)) {
                entries = None;
                unreadable = reason;
                return true;
            }

            entries = None;
            unreadable = null;
            return false;
        }
    }

    // Callers hold Gate. Cecil opens the dll with FileShare.Read, which blocks every write and
    // delete on it, and a side-car pdb is a second handle. Holding those for the process lifetime
    // would break a game patch, a Steam validate or a mod rebuild while the game runs, so the
    // module is disposed here. Every field an Entry carries is materialized before that happens.
    private static IReadOnlyList<Entry> Read(
        string path, Guid mvid, int token, MethodBase method, out string? unreadable) {
        ModuleDefinition? definition = null;
        try {
            definition = ModuleDefinition.ReadModule(path, new ReaderParameters {
                ReadingMode = ReadingMode.Deferred,
                SymbolReaderProvider = new DefaultSymbolReaderProvider(false),
                ThrowIfSymbolsAreNotMatching = true,
            });

            // The file on disk is a different build of the same assembly name, so its slot numbers
            // describe some other body than the one in memory.
            if (definition.Mvid != mvid) {
                Unreadable[mvid] = $"'{path}' is a different build of the module";
            } else if (!definition.HasSymbols) {
                Unreadable[mvid] = $"'{path}' carries no debug symbols";
            } else {
                IReadOnlyList<Entry> entries = Extract(definition, method);
                Cache[(mvid, token)] = entries;
                unreadable = null;
                return entries;
            }
        } catch (Exception ex) when (ex is SymbolsNotMatchingException or IOException
                                        or UnauthorizedAccessException or BadImageFormatException) {
            Unreadable[mvid] = $"'{path}' could not be read: {ex.Message}";
        } finally {
            definition?.Dispose();
        }

        unreadable = Unreadable[mvid];
        return None;
    }

    private static string? Locate(Module module, out string missing) {
        if (pathResolver is { } resolver) {
            string? resolved = resolver(module);
            if (!string.IsNullOrEmpty(resolved)) {
                if (File.Exists(resolved)) {
                    missing = string.Empty;
                    return resolved;
                }

                missing = $"the registered path resolver returned '{resolved}', which does not exist";
                return null;
            }
        }

        Assembly assembly = module.Assembly;
        if (assembly.IsDynamic) {
            missing = "the assembly was emitted at runtime and has no file on disk";
            return null;
        }

        string location = assembly.Location;
        if (string.IsNullOrEmpty(location)) {
            missing = "the assembly was loaded from bytes, so Assembly.Location is empty";
            return null;
        }

        if (!File.Exists(location)) {
            missing = $"'{location}' no longer exists";
            return null;
        }

        missing = string.Empty;
        return location;
    }

    private static IReadOnlyList<Entry> Extract(ModuleDefinition definition, MethodBase method) {
        if (definition.LookupToken(method.MetadataToken) is not MethodDefinition resolved || resolved.Name != method.Name) {
            return None;
        }

        MethodDebugInformation? debug = resolved.DebugInformation;
        if (debug is null || debug.Scope is null) {
            return None;
        }

        List<Entry> entries = new List<Entry>();
        Collect(debug.Scope, entries);
        return entries;
    }

    // Compiler temps are not source names, so a selector can never mean one of them.
    private static void Collect(ScopeDebugInformation scope, List<Entry> entries) {
        if (scope.HasVariables) {
            foreach (VariableDebugInformation variable in scope.Variables) {
                string name = variable.Name;
                if (variable.IsDebuggerHidden || string.IsNullOrEmpty(name)
                    || name[0] == '<' || name.StartsWith("CS$", StringComparison.Ordinal)) {
                    continue;
                }

                entries.Add((variable.Index, name, Offset(scope.Start, 0), Offset(scope.End, int.MaxValue)));
            }
        }

        if (scope.HasScopes) {
            foreach (ScopeDebugInformation child in scope.Scopes) {
                Collect(child, entries);
            }
        }
    }

    private static int Offset(InstructionOffset offset, int whenOpen) {
        return offset.IsEndOfMethod ? whenOpen : offset.Offset;
    }
}
