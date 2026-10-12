using System.Text.Json;
using dnlib.DotNet;
using Xunit;

namespace Concord.Host.Adapter.ForkTests;

public static class Measured {
    public const string Runtime = "10.0.10-waylithclr.dev";

    public const string InstallVariable = "CONCORD_GAME_INSTALL";

    private const string Installed = "/mnt/games/SteamLibrary/steamapps/common/RimWorld";

    public static string Root =>
        Environment.GetEnvironmentVariable(InstallVariable) is { Length: > 0 } overridden ? overridden : Installed;

    public static string Require(string name) {
        string path = Path.Combine(Root, name);
        Assert.True(
            File.Exists(path),
            $"no '{name}' under '{Root}'. These are early-warning tests that read the shipped WaylithCLR fork " +
            $"directly, so they need an install. Point {InstallVariable} at one, or run them on a machine that " +
            $"has it. Every behaviour they guard was measured on runtime '{Runtime}'.");

        return path;
    }
}

public class RuntimeBehaviourTests {
    private const string CoreLibFile = "System.Private.CoreLib.dll";

    private const string ConfigFile = "RimWorld.runtimeconfig.json";

    private const string HotReloadSwitch = "System.Reflection.Metadata.MetadataUpdater.IsSupported";

    private static readonly (string Behaviour, string Where, string Member)[] Surface = [
        (
            "reading a method body returns the ORIGINAL metadata IL, never the body currently live",
            "T14, and again as A17 finding 5",
            "System.Boolean WaylithClr.MethodBodyUpdater::TryGetMethodBody(System.Reflection.MethodBase,System.Span`1<WaylithClr.MethodBodyClause>,WaylithClr.MethodBodyView&)"),
        (
            "a foreign body write is undetectable: the updated-token list stays empty when a body is replaced",
            "T12",
            "System.Int32[] WaylithClr.MethodBodyUpdater::GetUpdatedMethodTokens(System.Reflection.Module)"),
        (
            "a foreign body write is undetectable: the metadata version counter never moves when a body is replaced",
            "T12",
            "System.Int32 WaylithClr.MethodBodyUpdater::get_MetadataUpdateVersion()"),
        (
            "publishing a body is last-write-wins, with no chaining",
            "T7, and the 200-cycle rerun in P1T2c",
            "System.Void WaylithClr.MethodBodyUpdater::ReplaceMethodBody(System.Reflection.MethodBase,System.ReadOnlySpan`1<System.Byte>,System.ReadOnlySpan`1<WaylithClr.ILOffsetMapEntry>)"),
        (
            "revert restores the ORIGINAL body, not the previous one, so rollback has to re-publish",
            "T8, and P1T2c",
            "System.Void WaylithClr.MethodBodyUpdater::Revert(System.Reflection.MethodBase)"),
        (
            "the offset map a publish carries is a pair of ints, which is what ForkBodyWriter builds reflectively",
            "the api capture",
            "System.Void WaylithClr.ILOffsetMapEntry::.ctor(System.Int32,System.Int32)"),
        (
            "the runtime's own switch gates body replacement, which is what RuntimeCapability reads to stay off",
            "the probe verdict table, IsSupported=True with no env var set",
            "System.Boolean WaylithClr.MethodBodyUpdater::get_IsSupported()"),
        (
            "composed IL can name a method in another assembly with one mint, no trampoline",
            "T6",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.Reflection.MethodBase)"),
        (
            "composed IL can name a field with one mint",
            "the api capture",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.Reflection.FieldInfo)"),
        (
            "composed IL can name a type with one mint",
            "the api capture",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.Type)"),
        (
            "composed IL can name a string literal with one mint",
            "the api capture",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.Module,System.String)"),
        (
            "a fat-header body can carry real locals, minted as a standalone signature",
            "T5, GetTokenForLocals = 0x11000013",
            "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForLocals(System.Reflection.Module,System.Byte[])"),
        (string.Empty, string.Empty, "System.Boolean WaylithClr.MethodBodyUpdater::TryGetMethodBody(System.RuntimeMethodHandle,System.Span`1<WaylithClr.MethodBodyClause>,WaylithClr.MethodBodyView&)"),
        (string.Empty, string.Empty, "System.RuntimeMethodHandle WaylithClr.MethodBodyUpdater::ResolveMethodDef(System.Reflection.Module,System.Int32)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::ReplaceMethodBody(System.RuntimeMethodHandle,System.ReadOnlySpan`1<System.Byte>,System.ReadOnlySpan`1<WaylithClr.ILOffsetMapEntry>)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::ReplaceMethodBodies(System.ReadOnlySpan`1<System.Reflection.MethodBase>,System.ReadOnlySpan`1<System.ReadOnlyMemory`1<System.Byte>>,System.ReadOnlySpan`1<System.ReadOnlyMemory`1<WaylithClr.ILOffsetMapEntry>>)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::ReplaceMethodBodies(System.ReadOnlySpan`1<System.Reflection.MethodBase>,System.ReadOnlySpan`1<System.Byte>,System.ReadOnlySpan`1<System.Int32>,System.ReadOnlySpan`1<WaylithClr.ILOffsetMapEntry>,System.ReadOnlySpan`1<System.Int32>)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::ReplaceMethodBodies(System.ReadOnlySpan`1<System.RuntimeMethodHandle>,System.ReadOnlySpan`1<System.Byte>,System.ReadOnlySpan`1<System.Int32>,System.ReadOnlySpan`1<WaylithClr.ILOffsetMapEntry>,System.ReadOnlySpan`1<System.Int32>)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::Revert(System.RuntimeMethodHandle)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::Revert(System.ReadOnlySpan`1<System.Reflection.MethodBase>)"),
        (string.Empty, string.Empty, "System.Void WaylithClr.MethodBodyUpdater::Revert(System.ReadOnlySpan`1<System.RuntimeMethodHandle>)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.MethodBase,System.Reflection.MethodBase)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.MethodBase,System.Reflection.FieldInfo)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.MethodBase,System.Type)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenFor(System.Reflection.MethodBase,System.String)"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyUpdater::GetTokenForLocals(System.Reflection.MethodBase,System.Byte[])"),
        (string.Empty, string.Empty, "System.ReadOnlySpan`1<System.Byte> WaylithClr.MethodBodyView::get_IL()"),
        (string.Empty, string.Empty, "System.ReadOnlySpan`1<System.Byte> WaylithClr.MethodBodyView::get_LocalSignature()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyView::get_LocalSignatureToken()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyView::get_MaxStackSize()"),
        (string.Empty, string.Empty, "System.Boolean WaylithClr.MethodBodyView::get_InitLocals()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyView::get_ClauseCount()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::Flags"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::TryOffset"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::TryLength"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::HandlerOffset"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::HandlerLength"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.MethodBodyClause::ClassTokenOrFilterOffset"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.ILOffsetMapEntry::get_OriginalOffset()"),
        (string.Empty, string.Empty, "System.Int32 WaylithClr.ILOffsetMapEntry::get_NewOffset()"),
    ];

    private static readonly string[] MeasuredTypes = [
        "WaylithClr.MethodBodyUpdater",
        "WaylithClr.MethodBodyView",
        "WaylithClr.MethodBodyClause",
        "WaylithClr.ILOffsetMapEntry",
    ];

    public static TheoryData<string, string, string> LoadBearing {
        get {
            TheoryData<string, string, string> rows = [];
            foreach ((string behaviour, string where, string member) in Surface) {
                if (behaviour.Length > 0) {
                    rows.Add(behaviour, where, member);
                }
            }

            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(LoadBearing))]
    public void TheEntryPointAMeasuredBehaviourWasObservedThroughIsStillOnTheShippedFork(
        string behaviour,
        string where,
        string member) {
        HashSet<string> shipped = PublicSurface(Measured.Require(CoreLibFile));

        Assert.True(
            shipped.Contains(member),
            $"the measured behaviour '{behaviour}' no longer has an entry point on the shipped fork. " +
            $"'{member}' is gone or changed signature, so the measurement it was taken through does not " +
            $"describe this runtime any more. It was measured as {where}, on runtime '{Measured.Runtime}'. " +
            "The adapter binds this api reflectively and falls back to the detour backend when a bind fails, " +
            "so a rename downgrades the whole backend silently instead of failing. Shipped surface under " +
            $"'{Measured.Root}':" +
            Listing(shipped.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void TheShippedForkExposesExactlyTheApiSurfaceTheProbesMeasured() {
        HashSet<string> shipped = PublicSurface(Measured.Require(CoreLibFile));
        HashSet<string> expected = [.. Surface.Select(entry => entry.Member)];

        List<string> gone = [.. expected.Except(shipped).Order(StringComparer.Ordinal)];
        List<string> added = [.. shipped.Except(expected).Order(StringComparer.Ordinal)];

        Assert.True(
            gone.Count == 0 && added.Count == 0,
            "the WaylithCLR body-replacement api changed shape under us. Every behaviour below was measured " +
            $"against the surface captured here, on runtime '{Measured.Runtime}'. An addition matters as much " +
            "as a removal: a Revert overload naming a version or a step would mean chaining arrived, and T7's " +
            "last-write-wins result would stop being the whole story. Decide what the change means before " +
            "trusting any measurement in this file." +
            $"\n  gone ({gone.Count}):" + Listing(gone) +
            $"\n  added ({added.Count}):" + Listing(added));
    }

    [Fact]
    public void TheAdapterStillNamesTheTypeTheShippedForkActuallyDeclares() {
        string[] parts = RuntimeCapability.UpdaterTypeName.Split(',', 2, StringSplitOptions.TrimEntries);
        ModuleDefMD corelib = ModuleDefMD.Load(Measured.Require(CoreLibFile));

        Assert.True(
            parts.Length == 2 && parts[1] == corelib.Assembly.Name,
            $"'{nameof(RuntimeCapability)}.{nameof(RuntimeCapability.UpdaterTypeName)}' is " +
            $"'{RuntimeCapability.UpdaterTypeName}', which does not name assembly '{corelib.Assembly.Name}' as " +
            $"shipped under '{Measured.Root}'. The adapter resolves that string with Type.GetType and reports " +
            "ApiAbsent when it misses, so a mismatch turns the waylith backend off with no error anywhere.");

        Assert.True(
            corelib.Find(parts[0], isReflectionName: false) is not null,
            $"'{parts[0]}' is not declared by '{corelib.Assembly.Name}' as shipped under '{Measured.Root}', but " +
            $"'{nameof(RuntimeCapability)}.{nameof(RuntimeCapability.UpdaterTypeName)}' still names it. Every " +
            "behaviour in this file was measured through that type. The adapter would report ApiAbsent and fall " +
            "back to detours without saying anything was wrong.");
    }

    [Fact]
    public void TheInstalledRuntimeIsStillTheForkBuildTheProbesRanOn() {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Measured.Require(ConfigFile)));
        JsonElement frameworks = config.RootElement.GetProperty("runtimeOptions").GetProperty("includedFrameworks");
        string? installed = frameworks[0].GetProperty("version").GetString();

        Assert.True(
            installed == Measured.Runtime,
            $"the fork runtime was rebuilt: '{ConfigFile}' under '{Measured.Root}' carries '{installed}', the " +
            $"probes measured '{Measured.Runtime}'. Nothing in this file has been re-checked against " +
            $"'{installed}'. Body replacement, revert semantics, generic sharing, R2R inline chasing and the " +
            "undetectability of a foreign write were all measured once, on the old build. Re-run the probes " +
            "before trusting them. The game build moving on its own is expected and does not trip this: this " +
            "line stayed put across every build the probes ran on.");
    }

    [Fact]
    public void HotReloadIsStillOffSoTheFlatVersionCounterMeansWhatItMeant() {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Measured.Require(ConfigFile)));
        JsonElement properties = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        Assert.True(
            properties.TryGetProperty(HotReloadSwitch, out JsonElement reload),
            $"'{HotReloadSwitch}' is no longer set in '{ConfigFile}' under '{Measured.Root}'. T12 measured " +
            "MetadataUpdateVersion staying at 0 and GetUpdatedMethodTokens staying empty across a replace and a " +
            "revert, with this switch pinned false. Whether that result still describes the runtime depends on " +
            "what the effective value is now.");

        Assert.True(
            reload.ValueKind == JsonValueKind.False,
            $"'{HotReloadSwitch}' is now '{reload}' in '{ConfigFile}' under '{Measured.Root}', not false. T12's " +
            "result, that replacing a body leaves MetadataUpdateVersion at 0 and GetUpdatedMethodTokens empty, " +
            "was measured with the stock metadata updater switched off. With it on there is a second writer " +
            "into the same counters, so 'the counter did not move' stops meaning 'nobody wrote a body'. " +
            "ForeignPatcherPolicy leans on that reading.");
    }

    private static HashSet<string> PublicSurface(string corelib) {
        ModuleDefMD module = ModuleDefMD.Load(corelib);
        HashSet<string> surface = [];
        foreach (string name in MeasuredTypes) {
            if (module.Find(name, isReflectionName: false) is not { } type) {
                continue;
            }

            foreach (MethodDef method in type.Methods) {
                if (method.IsPublic) {
                    surface.Add(method.FullName);
                }
            }

            foreach (FieldDef field in type.Fields) {
                if (field.IsPublic) {
                    surface.Add(field.FullName);
                }
            }
        }

        return surface;
    }

    private static string Listing(IEnumerable<string> entries) {
        return string.Concat(entries.Select(entry => "\n    " + entry));
    }
}
