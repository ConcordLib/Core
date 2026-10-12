using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace Concord.Waylith.OffsetMap;

public static class Probe {
    public const string UpdaterName = "WaylithClr.MethodBodyUpdater, System.Private.CoreLib";

    private const int SledBytes = 28;

    private static readonly Type Updater = Type.GetType(UpdaterName, false);

    private static Type entryType;

    private static ConstructorInfo entryCtor;

    private static Action<MethodBase, byte[], Array> replace;

    private static Func<Module, MethodBase, int> token;

    public static void Run() {
        Targets.Seed = 5;
        Report.Line("start updater=" + (Updater != null) + " supported=" + Supported() +
                    " runtime=" + Report.Flatten(RuntimeInformation.FrameworkDescription));

        if (!Bind()) {
            return;
        }

        Baseline();

        MethodInfo any = typeof(Targets).GetMethod("ControlLow");
        int ctorToken = token(any.Module, typeof(InvalidOperationException).GetConstructor(Type.EmptyTypes));
        int originalBytes = any.GetMethodBody().GetILAsByteArray().Length;

        int[] boundaries;
        int throwAt;
        int newobjAt;
        byte[] body = BuildBody(ctorToken, out boundaries, out newobjAt, out throwAt);

        Report.Line("body bytes=" + body.Length + " code-size=" + (body.Length - 1) +
                    " newobj-at=" + newobjAt + " throw-at=" + throwAt +
                    " boundaries=" + boundaries.Length + " ctor-token=0x" + ctorToken.ToString("X8", CultureInfo.InvariantCulture) +
                    " hex=" + Hex(body));

        Variant("control-low", "ControlLow", body, Dense(boundaries, 6));
        Variant("control-high", "ControlHigh", body, Dense(boundaries, 22));
        Variant("exact-at-throw", "ExactAtThrow", body, Pairs(0, 0, 14, throwAt));
        Variant("tail-only", "TailOnly", body, TailEntries(boundaries, throwAt));
        Variant("zero-only", "ZeroOnly", body, Pairs(0, 0));
        Variant("gap-around-throw", "GapAroundThrow", body, Pairs(6, 0, 24, throwAt + 1));
        Variant("empty", "EmptyMap", body, new List<int[]>());

        Report.Line("oob original-bytes=" + originalBytes);
        Variant("oob-just-past", "OutOfRangeNear", body, Pairs(3, 0, originalBytes, throwAt));
        Variant("oob-pair-a", "OutOfRangePairA", body, Pairs(500, 6, 900, throwAt));
        Variant("oob-pair-b", "OutOfRangePairB", body, Pairs(500, 6, 1800, throwAt));
        Variant("oob-far", "OutOfRangeFar", body, Pairs(3, 0, 100000000, throwAt));

        AdaptedRows();
    }

    private static void AdaptedRows() {
        MethodBase target = typeof(Adapted).GetMethod("Detonate");
        int band = target.GetMethodBody().GetILAsByteArray().Length;

        Report.Line("adapted present=" + Adapter.Present.ToString().ToLowerInvariant() +
                    " selected=" + Adapter.Selected() +
                    " md5=" + Adapter.Identity() +
                    " empty-map=" + Adapter.EmptyMapAlways() +
                    " switch=" + (Environment.GetEnvironmentVariable("CONCORD_WAYLITH_BACKEND") ?? "<unset>") +
                    " reason=" + Adapter.SelectionReason());
        Report.Line("adapted band original-bytes=" + band.ToString(CultureInfo.InvariantCulture) +
                    " head-owner=" + typeof(HeadPatch).FullName +
                    " tail-owner=" + typeof(TailPatch).FullName);

        try {
            Patcher.Apply(typeof(Probe).Assembly);
        } catch (Exception thrown) {
            Report.Line("adapted apply write=THREW type=" + Report.Cause(thrown).GetType().Name +
                        " message=" + Report.Flatten(Report.Cause(thrown).Message));
            return;
        }

        Report.Line("adapted published original-bytes=" + band.ToString(CultureInfo.InvariantCulture) +
                    " published-bytes=" + Adapter.PublishedBytes(target) +
                    " replaced=" + Adapter.Replaced(target).ToString().ToLowerInvariant() +
                    " owners=" + string.Join("/", Adapter.Owners(target)));

        Row("own", target, band, -1, false, false);
        Row("head", target, band, 1, true, false);
        Row("tail", target, band, 1, false, true);
    }

    private static void Row(string name, MethodBase target, int band, int seed, bool head, bool tail) {
        string line = "adapted-row " + name + " band=" + band.ToString(CultureInfo.InvariantCulture);
        Adapted.ArmHead = head;
        Adapted.ArmTail = tail;

        int returned;
        try {
            returned = Adapted.Detonate(seed);
        } catch (Exception thrown) {
            Adapted.ArmHead = false;
            Adapted.ArmTail = false;
            Exception cause = Report.Cause(thrown);
            string where;
            int il = OffsetOf(cause, target, out where);
            Report.Line(line + " threw=" + cause.GetType().Name +
                        " il=" + il.ToString(CultureInfo.InvariantCulture) +
                        " owner-at=" + Report.Flatten(Adapter.OwnerAt(target, il)) +
                        " " + where +
                        " head-seen=" + Adapted.HeadSeen.ToString(CultureInfo.InvariantCulture) +
                        " tail-seen=" + Adapted.TailSeen.ToString(CultureInfo.InvariantCulture));
            Frames(name, cause);
            return;
        }

        Adapted.ArmHead = false;
        Adapted.ArmTail = false;
        Report.Line(line + " threw=none returned=" + returned.ToString(CultureInfo.InvariantCulture) +
                    " head-seen=" + Adapted.HeadSeen.ToString(CultureInfo.InvariantCulture) +
                    " tail-seen=" + Adapted.TailSeen.ToString(CultureInfo.InvariantCulture));
    }

    private static int OffsetOf(Exception thrown, MethodBase target, out string where) {
        StackTrace trace = new StackTrace(thrown, true);
        for (int i = 0; i < trace.FrameCount; i++) {
            StackFrame frame = trace.GetFrame(i);
            MethodBase method = frame.GetMethod();
            if (method == null || method.Name != target.Name || method.DeclaringType != target.DeclaringType) {
                continue;
            }

            where = "frame=" + i.ToString(CultureInfo.InvariantCulture) +
                    " frames=" + trace.FrameCount.ToString(CultureInfo.InvariantCulture) +
                    " file=" + FileName(frame.GetFileName()) +
                    " line=" + frame.GetFileLineNumber().ToString(CultureInfo.InvariantCulture);
            return frame.GetILOffset();
        }

        where = "frame=absent frames=" + trace.FrameCount.ToString(CultureInfo.InvariantCulture) +
                " file=none line=none";
        return -1;
    }

    private static void Frames(string name, Exception thrown) {
        StackTrace trace = new StackTrace(thrown, true);
        for (int i = 0; i < trace.FrameCount && i < 4; i++) {
            StackFrame frame = trace.GetFrame(i);
            MethodBase method = frame.GetMethod();
            string described = method == null
                ? "<null>"
                : (method.DeclaringType == null ? method.Module.Name : method.DeclaringType.FullName) + "." + method.Name;
            Report.Line("adapted-frame " + name + " " + i.ToString(CultureInfo.InvariantCulture) +
                        " method=" + described +
                        " il=" + frame.GetILOffset().ToString(CultureInfo.InvariantCulture) +
                        " file=" + FileName(frame.GetFileName()) +
                        " line=" + frame.GetFileLineNumber().ToString(CultureInfo.InvariantCulture));
        }
    }

    private static bool Bind() {
        if (Updater == null) {
            Report.Line("bind replace=no-api revert=no-api token=no-api");
            return false;
        }

        MethodInfo found = FindReplace();
        if (found != null) {
            entryType = found.GetParameters()[2].ParameterType.GetGenericArguments()[0];
            entryCtor = entryType.GetConstructor(new[] { typeof(int), typeof(int) });
            if (entryCtor != null) {
                replace = BindReplace(found, entryType);
            }
        }

        MethodInfo minter = Updater.GetMethod(
            "GetTokenFor",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(Module), typeof(MethodBase) },
            null);
        if (minter != null) {
            token = (Func<Module, MethodBase, int>)minter.CreateDelegate(typeof(Func<Module, MethodBase, int>));
        }

        Report.Line("bind replace=" + (replace != null) + " token=" + (token != null) +
                    " entry=" + (entryType == null ? "none" : entryType.FullName) +
                    " ctor=" + (entryCtor == null ? "none" : Report.Flatten(entryCtor.ToString())) +
                    " map-optional=" + (found != null && found.GetParameters()[2].IsOptional));

        return replace != null && token != null;
    }

    private static MethodInfo FindReplace() {
        foreach (MethodInfo candidate in Updater.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            if (candidate.Name != "ReplaceMethodBody") {
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

    private static Action<MethodBase, byte[], Array> BindReplace(MethodInfo target, Type entry) {
        Type entries = entry.MakeArrayType();
        MethodInfo bytes = typeof(ReadOnlySpan<byte>).GetMethod("op_Implicit", new[] { typeof(byte[]) });
        MethodInfo map = typeof(ReadOnlySpan<>).MakeGenericType(entry).GetMethod("op_Implicit", new[] { entries });
        if (bytes == null || map == null) {
            return null;
        }

        DynamicMethod call = new DynamicMethod(
            "OffsetMapProbeReplace",
            typeof(void),
            new[] { typeof(MethodBase), typeof(byte[]), typeof(Array) },
            typeof(Probe).Module,
            true);

        ILGenerator il = call.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, bytes);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, entries);
        il.Emit(OpCodes.Call, map);
        il.Emit(OpCodes.Call, target);
        il.Emit(OpCodes.Ret);
        return (Action<MethodBase, byte[], Array>)call.CreateDelegate(typeof(Action<MethodBase, byte[], Array>));
    }

    private static byte[] BuildBody(int ctorToken, out int[] boundaries, out int newobjAt, out int throwAt) {
        List<byte> il = new List<byte>();
        List<int> marks = new List<int>();

        for (int i = 0; i < SledBytes; i++) {
            marks.Add(il.Count);
            il.Add(0x00);
        }

        newobjAt = il.Count;
        marks.Add(il.Count);
        il.Add(0x73);
        il.Add((byte)ctorToken);
        il.Add((byte)(ctorToken >> 8));
        il.Add((byte)(ctorToken >> 16));
        il.Add((byte)(ctorToken >> 24));

        throwAt = il.Count;
        marks.Add(il.Count);
        il.Add(0x7A);

        marks.Add(il.Count);
        il.Add(0x00);
        marks.Add(il.Count);
        il.Add(0x00);
        marks.Add(il.Count);
        il.Add(0x1F);
        il.Add(42);
        marks.Add(il.Count);
        il.Add(0x2A);

        byte[] body = new byte[il.Count + 1];
        body[0] = (byte)((il.Count << 2) | 0x02);
        il.CopyTo(body, 1);
        boundaries = marks.ToArray();
        return body;
    }

    private static List<int[]> Dense(int[] boundaries, int original) {
        List<int[]> map = new List<int[]>();
        foreach (int at in boundaries) {
            map.Add(new[] { original, at });
        }

        return map;
    }

    private static List<int[]> TailEntries(int[] boundaries, int throwAt) {
        List<int[]> map = new List<int[]>();
        int original = 5;
        foreach (int at in boundaries) {
            if (at <= throwAt) {
                continue;
            }

            map.Add(new[] { original, at });
            original += 8;
        }

        return map;
    }

    private static List<int[]> Pairs(params int[] flat) {
        List<int[]> map = new List<int[]>();
        for (int i = 0; i < flat.Length; i += 2) {
            map.Add(new[] { flat[i], flat[i + 1] });
        }

        return map;
    }

    private static void Variant(string name, string method, byte[] body, List<int[]> map) {
        MethodInfo target = typeof(Targets).GetMethod(method);
        int originalBytes = target.GetMethodBody().GetILAsByteArray().Length;
        string head = "variant " + name + " entries=" + map.Count + " map=" + Show(map) +
                      " original-bytes=" + originalBytes;

        try {
            Array entries = Array.CreateInstance(entryType, map.Count);
            for (int i = 0; i < map.Count; i++) {
                entries.SetValue(entryCtor.Invoke(new object[] { map[i][0], map[i][1] }), i);
            }

            replace(target, body, entries);
        } catch (Exception thrown) {
            Report.Line(head + " write=THREW type=" + Report.Cause(thrown).GetType().Name +
                        " message=" + Report.Flatten(Report.Cause(thrown).Message));
            return;
        }

        try {
            object returned = target.Invoke(null, null);
            Report.Line(head + " write=ok threw=none returned=" + returned);
        } catch (Exception thrown) {
            Exception cause = Report.Cause(thrown);
            Report.Line(head + " write=ok threw=" + cause.GetType().Name + " " + Frame(cause, target));
        }
    }

    private static void Baseline() {
        MethodInfo target = typeof(Targets).GetMethod("Baseline");
        int originalBytes = target.GetMethodBody().GetILAsByteArray().Length;
        try {
            target.Invoke(null, null);
            Report.Line("baseline original-bytes=" + originalBytes + " threw=none");
        } catch (Exception thrown) {
            Exception cause = Report.Cause(thrown);
            Report.Line("baseline original-bytes=" + originalBytes + " threw=" + cause.GetType().Name +
                        " " + Frame(cause, target));
        }
    }

    private static string Frame(Exception thrown, MethodBase target) {
        StackTrace trace = new StackTrace(thrown, true);
        for (int i = 0; i < trace.FrameCount; i++) {
            StackFrame frame = trace.GetFrame(i);
            MethodBase method = frame.GetMethod();
            if (method == null || method.Name != target.Name || method.DeclaringType != target.DeclaringType) {
                continue;
            }

            return "frame=" + i + " frames=" + trace.FrameCount + " il=" + frame.GetILOffset() +
                   " file=" + FileName(frame.GetFileName()) + " line=" + frame.GetFileLineNumber();
        }

        return "frame=absent frames=" + trace.FrameCount + " il=none file=none line=none";
    }

    private static string FileName(string path) {
        if (string.IsNullOrEmpty(path)) {
            return "none";
        }

        int cut = path.LastIndexOfAny(new[] { '/', '\\' });
        return cut < 0 ? path : path.Substring(cut + 1);
    }

    private static string Show(List<int[]> map) {
        if (map.Count == 0) {
            return "-";
        }

        StringBuilder text = new StringBuilder();
        int shown = map.Count > 4 ? 4 : map.Count;
        for (int i = 0; i < shown; i++) {
            if (i > 0) {
                text.Append(',');
            }

            text.Append(map[i][1]).Append("->").Append(map[i][0]);
        }

        if (map.Count > shown) {
            text.Append(",..,").Append(map[map.Count - 1][1]).Append("->").Append(map[map.Count - 1][0]);
        }

        return text.ToString();
    }

    private static string Hex(byte[] bytes) {
        StringBuilder text = new StringBuilder(bytes.Length * 2);
        foreach (byte value in bytes) {
            text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    private static string Supported() {
        if (Updater == null) {
            return "no-api";
        }

        PropertyInfo property = Updater.GetProperty("IsSupported", BindingFlags.Public | BindingFlags.Static);
        if (property == null) {
            return "no-entry";
        }

        try {
            return Convert.ToString(property.GetValue(null), CultureInfo.InvariantCulture);
        } catch (Exception thrown) {
            return "THREW " + Report.Cause(thrown).GetType().Name;
        }
    }
}
