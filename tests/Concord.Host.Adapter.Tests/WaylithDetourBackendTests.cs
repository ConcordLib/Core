using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using Concord.Detour;
using Concord.Emit;
using Xunit;
using OpCode = System.Reflection.Emit.OpCode;

namespace Concord.Host.Adapter.Tests;

public static class FakeUpdater {
    public static readonly List<(MethodBase Target, byte[] Body, Entry[] Map)> Writes = [];

    public static readonly List<MethodBase> Reverts = [];

    public static readonly List<bool> ClaimedAtWrite = [];

    public static void ReplaceMethodBody(MethodBase method, ReadOnlySpan<byte> methodBody, ReadOnlySpan<Entry> ilOffsetMap) {
        ClaimedAtWrite.Add(WaylithDetourBackend.Writers.HoldsClaim(method));
        Writes.Add((method, methodBody.ToArray(), ilOffsetMap.ToArray()));
    }

    public static void Revert(MethodBase method) {
        Reverts.Add(method);
    }

    public static void Reset() {
        Writes.Clear();
        Reverts.Clear();
        ClaimedAtWrite.Clear();
    }

    public readonly struct Entry {
        public Entry(int originalOffset, int newOffset) {
            OriginalOffset = originalOffset;
            NewOffset = newOffset;
        }

        public int OriginalOffset { get; }

        public int NewOffset { get; }
    }
}

public static class UntouchedRevertUpdater {
    public static readonly List<MethodBase> Reverts = [];

    public static void ReplaceMethodBody(MethodBase method, ReadOnlySpan<byte> methodBody, ReadOnlySpan<FakeUpdater.Entry> ilOffsetMap) {
    }

    public static void Revert(MethodBase method) {
        Reverts.Add(method);
        throw new InvalidOperationException("that method never carried a replaced body");
    }
}

public static class ThrowingUpdater {
    public static void ReplaceMethodBody(MethodBase method, ReadOnlySpan<byte> methodBody, ReadOnlySpan<FakeUpdater.Entry> ilOffsetMap) {
        throw new InvalidOperationException("this runtime refuses that method");
    }

    public static void Revert(MethodBase method) {
    }
}

public static class ConcurrentUpdater {
    public static readonly ConcurrentQueue<MethodBase> Writes = new ConcurrentQueue<MethodBase>();

    public static readonly ConcurrentQueue<int> Sizes = new ConcurrentQueue<int>();

    public static readonly ConcurrentQueue<MethodBase> Reverts = new ConcurrentQueue<MethodBase>();

    public static void ReplaceMethodBody(MethodBase method, ReadOnlySpan<byte> methodBody, ReadOnlySpan<FakeUpdater.Entry> ilOffsetMap) {
        Writes.Enqueue(method);
        Sizes.Enqueue(methodBody.Length);
    }

    public static void Revert(MethodBase method) {
        Reverts.Enqueue(method);
    }

    public static void Reset() {
        Writes.Clear();
        Sizes.Clear();
        Reverts.Clear();
    }
}

public static class RefusingUpdater {
    public static void ReplaceMethodBody(MethodBase method, ReadOnlySpan<byte> methodBody, ReadOnlySpan<FakeUpdater.Entry> ilOffsetMap) {
        throw new InvalidOperationException("this runtime refuses that method");
    }

    public static void Revert(MethodBase method) {
        throw new InvalidOperationException("nothing to take back off");
    }
}

public static class PatchSubject {
    public static int Touched { get; set; }

    public static void Marker() {
        Touched++;
    }

    public static void SecondMarker() {
        Touched += 2;
    }

    public static int Sum(int left, int right) {
        return left + right;
    }

    public static int Spanned(int left, int right) {
        int first = left + right;
        int second = first * left;
        int third = second - right;
        return first + second + third;
    }

    public static int Refused(int left) {
        return left * 2;
    }

    public static int Forwarded(int left) {
        return left * 3;
    }

    public static int Generated(int left) {
        return left * 4;
    }

    public static int Taken(int left) {
        return left * 5;
    }

    public static int Denied(int left) {
        return left * 6;
    }

    public static int Claimed(int left) {
        return left * 7;
    }

    public static int Released(int left) {
        return left * 8;
    }

    public static int Contended(int left) {
        return left * 9;
    }

    public static int Owned(int left) {
        return left * 10;
    }

    public static int Attributed(int left) {
        return left * 11;
    }

    public static int Dropped(int left) {
        return left * 12;
    }

    public static int Refuted(int left) {
        return left * 13;
    }

    public static int Surrendered(int left) {
        return left * 14;
    }

    public static int Seized(int left) {
        return left * 15;
    }

    public static int Untouched(int left) {
        return left * 16;
    }

    public static int Questioned(int left) {
        return left * 17;
    }

    public static int Withheld(int left) {
        return left * 18;
    }

    public static int Handed(int left) {
        return left * 19;
    }

    public static int Doubled(int left) {
        return left * 20;
    }

    public static int Overlaid(int left) {
        return left * 29;
    }

    public static int Collided(int left) {
        return left * 30;
    }

    public static int Rolled(int left) {
        return left * 21;
    }

    public static int Tallied(int left) {
        return left * 22;
    }

    public static int Shelved(int left) {
        return left * 23;
    }

    public static int Raced(int left) {
        return left * 24;
    }

    public static int Mismatched(int left, int right) {
        return left - right;
    }

    public static int Twinned(int left) {
        return left * 25;
    }

    public static int Preempted(int left) {
        return left * 26;
    }

    public static int Balked(int left) {
        return left * 27;
    }

    public static void LocalAtHead([Local] int slot) {
        Touched += slot;
    }
}

public abstract class Shaped {
    public abstract int Missing(int left);
}

public sealed class Boxed<T> {
    public int Ping() {
        return 1;
    }
}

public static class RaceSubjects {
    public const int Count = 300;

    private static readonly MethodInfo[] Pool = Harvest();

    public static MethodInfo At(int round) {
        return Pool[round];
    }

    private static MethodInfo[] Harvest() {
        MethodInfo[] found = typeof(RaceSubjects)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("Round", StringComparison.Ordinal))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        if (found.Length != Count) {
            throw new InvalidOperationException(
                $"RaceSubjects holds {found.Length} fresh targets and {Count} were declared. One race per " +
                "target is what keeps the gap between a guard and a publish open, so a reused target would " +
                "be refused by the guard alone and the race would never be entered.");
        }

        return found;
    }

    public static int Round000(int left) { return left + 1; }
    public static int Round001(int left) { return left + 2; }
    public static int Round002(int left) { return left + 3; }
    public static int Round003(int left) { return left + 4; }
    public static int Round004(int left) { return left + 5; }
    public static int Round005(int left) { return left + 6; }
    public static int Round006(int left) { return left + 7; }
    public static int Round007(int left) { return left + 8; }
    public static int Round008(int left) { return left + 9; }
    public static int Round009(int left) { return left + 10; }
    public static int Round010(int left) { return left + 11; }
    public static int Round011(int left) { return left + 12; }
    public static int Round012(int left) { return left + 13; }
    public static int Round013(int left) { return left + 14; }
    public static int Round014(int left) { return left + 15; }
    public static int Round015(int left) { return left + 16; }
    public static int Round016(int left) { return left + 17; }
    public static int Round017(int left) { return left + 18; }
    public static int Round018(int left) { return left + 19; }
    public static int Round019(int left) { return left + 20; }
    public static int Round020(int left) { return left + 21; }
    public static int Round021(int left) { return left + 22; }
    public static int Round022(int left) { return left + 23; }
    public static int Round023(int left) { return left + 24; }
    public static int Round024(int left) { return left + 25; }
    public static int Round025(int left) { return left + 26; }
    public static int Round026(int left) { return left + 27; }
    public static int Round027(int left) { return left + 28; }
    public static int Round028(int left) { return left + 29; }
    public static int Round029(int left) { return left + 30; }
    public static int Round030(int left) { return left + 31; }
    public static int Round031(int left) { return left + 32; }
    public static int Round032(int left) { return left + 33; }
    public static int Round033(int left) { return left + 34; }
    public static int Round034(int left) { return left + 35; }
    public static int Round035(int left) { return left + 36; }
    public static int Round036(int left) { return left + 37; }
    public static int Round037(int left) { return left + 38; }
    public static int Round038(int left) { return left + 39; }
    public static int Round039(int left) { return left + 40; }
    public static int Round040(int left) { return left + 41; }
    public static int Round041(int left) { return left + 42; }
    public static int Round042(int left) { return left + 43; }
    public static int Round043(int left) { return left + 44; }
    public static int Round044(int left) { return left + 45; }
    public static int Round045(int left) { return left + 46; }
    public static int Round046(int left) { return left + 47; }
    public static int Round047(int left) { return left + 48; }
    public static int Round048(int left) { return left + 49; }
    public static int Round049(int left) { return left + 50; }
    public static int Round050(int left) { return left + 51; }
    public static int Round051(int left) { return left + 52; }
    public static int Round052(int left) { return left + 53; }
    public static int Round053(int left) { return left + 54; }
    public static int Round054(int left) { return left + 55; }
    public static int Round055(int left) { return left + 56; }
    public static int Round056(int left) { return left + 57; }
    public static int Round057(int left) { return left + 58; }
    public static int Round058(int left) { return left + 59; }
    public static int Round059(int left) { return left + 60; }
    public static int Round060(int left) { return left + 61; }
    public static int Round061(int left) { return left + 62; }
    public static int Round062(int left) { return left + 63; }
    public static int Round063(int left) { return left + 64; }
    public static int Round064(int left) { return left + 65; }
    public static int Round065(int left) { return left + 66; }
    public static int Round066(int left) { return left + 67; }
    public static int Round067(int left) { return left + 68; }
    public static int Round068(int left) { return left + 69; }
    public static int Round069(int left) { return left + 70; }
    public static int Round070(int left) { return left + 71; }
    public static int Round071(int left) { return left + 72; }
    public static int Round072(int left) { return left + 73; }
    public static int Round073(int left) { return left + 74; }
    public static int Round074(int left) { return left + 75; }
    public static int Round075(int left) { return left + 76; }
    public static int Round076(int left) { return left + 77; }
    public static int Round077(int left) { return left + 78; }
    public static int Round078(int left) { return left + 79; }
    public static int Round079(int left) { return left + 80; }
    public static int Round080(int left) { return left + 81; }
    public static int Round081(int left) { return left + 82; }
    public static int Round082(int left) { return left + 83; }
    public static int Round083(int left) { return left + 84; }
    public static int Round084(int left) { return left + 85; }
    public static int Round085(int left) { return left + 86; }
    public static int Round086(int left) { return left + 87; }
    public static int Round087(int left) { return left + 88; }
    public static int Round088(int left) { return left + 89; }
    public static int Round089(int left) { return left + 90; }
    public static int Round090(int left) { return left + 91; }
    public static int Round091(int left) { return left + 92; }
    public static int Round092(int left) { return left + 93; }
    public static int Round093(int left) { return left + 94; }
    public static int Round094(int left) { return left + 95; }
    public static int Round095(int left) { return left + 96; }
    public static int Round096(int left) { return left + 97; }
    public static int Round097(int left) { return left + 98; }
    public static int Round098(int left) { return left + 99; }
    public static int Round099(int left) { return left + 100; }
    public static int Round100(int left) { return left + 101; }
    public static int Round101(int left) { return left + 102; }
    public static int Round102(int left) { return left + 103; }
    public static int Round103(int left) { return left + 104; }
    public static int Round104(int left) { return left + 105; }
    public static int Round105(int left) { return left + 106; }
    public static int Round106(int left) { return left + 107; }
    public static int Round107(int left) { return left + 108; }
    public static int Round108(int left) { return left + 109; }
    public static int Round109(int left) { return left + 110; }
    public static int Round110(int left) { return left + 111; }
    public static int Round111(int left) { return left + 112; }
    public static int Round112(int left) { return left + 113; }
    public static int Round113(int left) { return left + 114; }
    public static int Round114(int left) { return left + 115; }
    public static int Round115(int left) { return left + 116; }
    public static int Round116(int left) { return left + 117; }
    public static int Round117(int left) { return left + 118; }
    public static int Round118(int left) { return left + 119; }
    public static int Round119(int left) { return left + 120; }
    public static int Round120(int left) { return left + 121; }
    public static int Round121(int left) { return left + 122; }
    public static int Round122(int left) { return left + 123; }
    public static int Round123(int left) { return left + 124; }
    public static int Round124(int left) { return left + 125; }
    public static int Round125(int left) { return left + 126; }
    public static int Round126(int left) { return left + 127; }
    public static int Round127(int left) { return left + 128; }
    public static int Round128(int left) { return left + 129; }
    public static int Round129(int left) { return left + 130; }
    public static int Round130(int left) { return left + 131; }
    public static int Round131(int left) { return left + 132; }
    public static int Round132(int left) { return left + 133; }
    public static int Round133(int left) { return left + 134; }
    public static int Round134(int left) { return left + 135; }
    public static int Round135(int left) { return left + 136; }
    public static int Round136(int left) { return left + 137; }
    public static int Round137(int left) { return left + 138; }
    public static int Round138(int left) { return left + 139; }
    public static int Round139(int left) { return left + 140; }
    public static int Round140(int left) { return left + 141; }
    public static int Round141(int left) { return left + 142; }
    public static int Round142(int left) { return left + 143; }
    public static int Round143(int left) { return left + 144; }
    public static int Round144(int left) { return left + 145; }
    public static int Round145(int left) { return left + 146; }
    public static int Round146(int left) { return left + 147; }
    public static int Round147(int left) { return left + 148; }
    public static int Round148(int left) { return left + 149; }
    public static int Round149(int left) { return left + 150; }
    public static int Round150(int left) { return left + 151; }
    public static int Round151(int left) { return left + 152; }
    public static int Round152(int left) { return left + 153; }
    public static int Round153(int left) { return left + 154; }
    public static int Round154(int left) { return left + 155; }
    public static int Round155(int left) { return left + 156; }
    public static int Round156(int left) { return left + 157; }
    public static int Round157(int left) { return left + 158; }
    public static int Round158(int left) { return left + 159; }
    public static int Round159(int left) { return left + 160; }
    public static int Round160(int left) { return left + 161; }
    public static int Round161(int left) { return left + 162; }
    public static int Round162(int left) { return left + 163; }
    public static int Round163(int left) { return left + 164; }
    public static int Round164(int left) { return left + 165; }
    public static int Round165(int left) { return left + 166; }
    public static int Round166(int left) { return left + 167; }
    public static int Round167(int left) { return left + 168; }
    public static int Round168(int left) { return left + 169; }
    public static int Round169(int left) { return left + 170; }
    public static int Round170(int left) { return left + 171; }
    public static int Round171(int left) { return left + 172; }
    public static int Round172(int left) { return left + 173; }
    public static int Round173(int left) { return left + 174; }
    public static int Round174(int left) { return left + 175; }
    public static int Round175(int left) { return left + 176; }
    public static int Round176(int left) { return left + 177; }
    public static int Round177(int left) { return left + 178; }
    public static int Round178(int left) { return left + 179; }
    public static int Round179(int left) { return left + 180; }
    public static int Round180(int left) { return left + 181; }
    public static int Round181(int left) { return left + 182; }
    public static int Round182(int left) { return left + 183; }
    public static int Round183(int left) { return left + 184; }
    public static int Round184(int left) { return left + 185; }
    public static int Round185(int left) { return left + 186; }
    public static int Round186(int left) { return left + 187; }
    public static int Round187(int left) { return left + 188; }
    public static int Round188(int left) { return left + 189; }
    public static int Round189(int left) { return left + 190; }
    public static int Round190(int left) { return left + 191; }
    public static int Round191(int left) { return left + 192; }
    public static int Round192(int left) { return left + 193; }
    public static int Round193(int left) { return left + 194; }
    public static int Round194(int left) { return left + 195; }
    public static int Round195(int left) { return left + 196; }
    public static int Round196(int left) { return left + 197; }
    public static int Round197(int left) { return left + 198; }
    public static int Round198(int left) { return left + 199; }
    public static int Round199(int left) { return left + 200; }
    public static int Round200(int left) { return left + 201; }
    public static int Round201(int left) { return left + 202; }
    public static int Round202(int left) { return left + 203; }
    public static int Round203(int left) { return left + 204; }
    public static int Round204(int left) { return left + 205; }
    public static int Round205(int left) { return left + 206; }
    public static int Round206(int left) { return left + 207; }
    public static int Round207(int left) { return left + 208; }
    public static int Round208(int left) { return left + 209; }
    public static int Round209(int left) { return left + 210; }
    public static int Round210(int left) { return left + 211; }
    public static int Round211(int left) { return left + 212; }
    public static int Round212(int left) { return left + 213; }
    public static int Round213(int left) { return left + 214; }
    public static int Round214(int left) { return left + 215; }
    public static int Round215(int left) { return left + 216; }
    public static int Round216(int left) { return left + 217; }
    public static int Round217(int left) { return left + 218; }
    public static int Round218(int left) { return left + 219; }
    public static int Round219(int left) { return left + 220; }
    public static int Round220(int left) { return left + 221; }
    public static int Round221(int left) { return left + 222; }
    public static int Round222(int left) { return left + 223; }
    public static int Round223(int left) { return left + 224; }
    public static int Round224(int left) { return left + 225; }
    public static int Round225(int left) { return left + 226; }
    public static int Round226(int left) { return left + 227; }
    public static int Round227(int left) { return left + 228; }
    public static int Round228(int left) { return left + 229; }
    public static int Round229(int left) { return left + 230; }
    public static int Round230(int left) { return left + 231; }
    public static int Round231(int left) { return left + 232; }
    public static int Round232(int left) { return left + 233; }
    public static int Round233(int left) { return left + 234; }
    public static int Round234(int left) { return left + 235; }
    public static int Round235(int left) { return left + 236; }
    public static int Round236(int left) { return left + 237; }
    public static int Round237(int left) { return left + 238; }
    public static int Round238(int left) { return left + 239; }
    public static int Round239(int left) { return left + 240; }
    public static int Round240(int left) { return left + 241; }
    public static int Round241(int left) { return left + 242; }
    public static int Round242(int left) { return left + 243; }
    public static int Round243(int left) { return left + 244; }
    public static int Round244(int left) { return left + 245; }
    public static int Round245(int left) { return left + 246; }
    public static int Round246(int left) { return left + 247; }
    public static int Round247(int left) { return left + 248; }
    public static int Round248(int left) { return left + 249; }
    public static int Round249(int left) { return left + 250; }
    public static int Round250(int left) { return left + 251; }
    public static int Round251(int left) { return left + 252; }
    public static int Round252(int left) { return left + 253; }
    public static int Round253(int left) { return left + 254; }
    public static int Round254(int left) { return left + 255; }
    public static int Round255(int left) { return left + 256; }
    public static int Round256(int left) { return left + 257; }
    public static int Round257(int left) { return left + 258; }
    public static int Round258(int left) { return left + 259; }
    public static int Round259(int left) { return left + 260; }
    public static int Round260(int left) { return left + 261; }
    public static int Round261(int left) { return left + 262; }
    public static int Round262(int left) { return left + 263; }
    public static int Round263(int left) { return left + 264; }
    public static int Round264(int left) { return left + 265; }
    public static int Round265(int left) { return left + 266; }
    public static int Round266(int left) { return left + 267; }
    public static int Round267(int left) { return left + 268; }
    public static int Round268(int left) { return left + 269; }
    public static int Round269(int left) { return left + 270; }
    public static int Round270(int left) { return left + 271; }
    public static int Round271(int left) { return left + 272; }
    public static int Round272(int left) { return left + 273; }
    public static int Round273(int left) { return left + 274; }
    public static int Round274(int left) { return left + 275; }
    public static int Round275(int left) { return left + 276; }
    public static int Round276(int left) { return left + 277; }
    public static int Round277(int left) { return left + 278; }
    public static int Round278(int left) { return left + 279; }
    public static int Round279(int left) { return left + 280; }
    public static int Round280(int left) { return left + 281; }
    public static int Round281(int left) { return left + 282; }
    public static int Round282(int left) { return left + 283; }
    public static int Round283(int left) { return left + 284; }
    public static int Round284(int left) { return left + 285; }
    public static int Round285(int left) { return left + 286; }
    public static int Round286(int left) { return left + 287; }
    public static int Round287(int left) { return left + 288; }
    public static int Round288(int left) { return left + 289; }
    public static int Round289(int left) { return left + 290; }
    public static int Round290(int left) { return left + 291; }
    public static int Round291(int left) { return left + 292; }
    public static int Round292(int left) { return left + 293; }
    public static int Round293(int left) { return left + 294; }
    public static int Round294(int left) { return left + 295; }
    public static int Round295(int left) { return left + 296; }
    public static int Round296(int left) { return left + 297; }
    public static int Round297(int left) { return left + 298; }
    public static int Round298(int left) { return left + 299; }
    public static int Round299(int left) { return left + 300; }
}

public static class RevertRaceSubjects {
    public const int Count = 300;

    private static readonly MethodInfo[] Pool = Harvest();

    public static MethodInfo At(int round) {
        return Pool[round];
    }

    private static MethodInfo[] Harvest() {
        MethodInfo[] found = typeof(RevertRaceSubjects)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("Round", StringComparison.Ordinal))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        if (found.Length != Count) {
            throw new InvalidOperationException(
                $"RevertRaceSubjects holds {found.Length} fresh targets and {Count} were declared. These targets " +
                "stay separate from RaceSubjects on purpose: a target shared between two races carries the other " +
                "race's forwarder and claim state, so one race's outcome would depend on which test ran first.");
        }

        return found;
    }

    public static int Round000(int left) { return left + 1; }
    public static int Round001(int left) { return left + 2; }
    public static int Round002(int left) { return left + 3; }
    public static int Round003(int left) { return left + 4; }
    public static int Round004(int left) { return left + 5; }
    public static int Round005(int left) { return left + 6; }
    public static int Round006(int left) { return left + 7; }
    public static int Round007(int left) { return left + 8; }
    public static int Round008(int left) { return left + 9; }
    public static int Round009(int left) { return left + 10; }
    public static int Round010(int left) { return left + 11; }
    public static int Round011(int left) { return left + 12; }
    public static int Round012(int left) { return left + 13; }
    public static int Round013(int left) { return left + 14; }
    public static int Round014(int left) { return left + 15; }
    public static int Round015(int left) { return left + 16; }
    public static int Round016(int left) { return left + 17; }
    public static int Round017(int left) { return left + 18; }
    public static int Round018(int left) { return left + 19; }
    public static int Round019(int left) { return left + 20; }
    public static int Round020(int left) { return left + 21; }
    public static int Round021(int left) { return left + 22; }
    public static int Round022(int left) { return left + 23; }
    public static int Round023(int left) { return left + 24; }
    public static int Round024(int left) { return left + 25; }
    public static int Round025(int left) { return left + 26; }
    public static int Round026(int left) { return left + 27; }
    public static int Round027(int left) { return left + 28; }
    public static int Round028(int left) { return left + 29; }
    public static int Round029(int left) { return left + 30; }
    public static int Round030(int left) { return left + 31; }
    public static int Round031(int left) { return left + 32; }
    public static int Round032(int left) { return left + 33; }
    public static int Round033(int left) { return left + 34; }
    public static int Round034(int left) { return left + 35; }
    public static int Round035(int left) { return left + 36; }
    public static int Round036(int left) { return left + 37; }
    public static int Round037(int left) { return left + 38; }
    public static int Round038(int left) { return left + 39; }
    public static int Round039(int left) { return left + 40; }
    public static int Round040(int left) { return left + 41; }
    public static int Round041(int left) { return left + 42; }
    public static int Round042(int left) { return left + 43; }
    public static int Round043(int left) { return left + 44; }
    public static int Round044(int left) { return left + 45; }
    public static int Round045(int left) { return left + 46; }
    public static int Round046(int left) { return left + 47; }
    public static int Round047(int left) { return left + 48; }
    public static int Round048(int left) { return left + 49; }
    public static int Round049(int left) { return left + 50; }
    public static int Round050(int left) { return left + 51; }
    public static int Round051(int left) { return left + 52; }
    public static int Round052(int left) { return left + 53; }
    public static int Round053(int left) { return left + 54; }
    public static int Round054(int left) { return left + 55; }
    public static int Round055(int left) { return left + 56; }
    public static int Round056(int left) { return left + 57; }
    public static int Round057(int left) { return left + 58; }
    public static int Round058(int left) { return left + 59; }
    public static int Round059(int left) { return left + 60; }
    public static int Round060(int left) { return left + 61; }
    public static int Round061(int left) { return left + 62; }
    public static int Round062(int left) { return left + 63; }
    public static int Round063(int left) { return left + 64; }
    public static int Round064(int left) { return left + 65; }
    public static int Round065(int left) { return left + 66; }
    public static int Round066(int left) { return left + 67; }
    public static int Round067(int left) { return left + 68; }
    public static int Round068(int left) { return left + 69; }
    public static int Round069(int left) { return left + 70; }
    public static int Round070(int left) { return left + 71; }
    public static int Round071(int left) { return left + 72; }
    public static int Round072(int left) { return left + 73; }
    public static int Round073(int left) { return left + 74; }
    public static int Round074(int left) { return left + 75; }
    public static int Round075(int left) { return left + 76; }
    public static int Round076(int left) { return left + 77; }
    public static int Round077(int left) { return left + 78; }
    public static int Round078(int left) { return left + 79; }
    public static int Round079(int left) { return left + 80; }
    public static int Round080(int left) { return left + 81; }
    public static int Round081(int left) { return left + 82; }
    public static int Round082(int left) { return left + 83; }
    public static int Round083(int left) { return left + 84; }
    public static int Round084(int left) { return left + 85; }
    public static int Round085(int left) { return left + 86; }
    public static int Round086(int left) { return left + 87; }
    public static int Round087(int left) { return left + 88; }
    public static int Round088(int left) { return left + 89; }
    public static int Round089(int left) { return left + 90; }
    public static int Round090(int left) { return left + 91; }
    public static int Round091(int left) { return left + 92; }
    public static int Round092(int left) { return left + 93; }
    public static int Round093(int left) { return left + 94; }
    public static int Round094(int left) { return left + 95; }
    public static int Round095(int left) { return left + 96; }
    public static int Round096(int left) { return left + 97; }
    public static int Round097(int left) { return left + 98; }
    public static int Round098(int left) { return left + 99; }
    public static int Round099(int left) { return left + 100; }
    public static int Round100(int left) { return left + 101; }
    public static int Round101(int left) { return left + 102; }
    public static int Round102(int left) { return left + 103; }
    public static int Round103(int left) { return left + 104; }
    public static int Round104(int left) { return left + 105; }
    public static int Round105(int left) { return left + 106; }
    public static int Round106(int left) { return left + 107; }
    public static int Round107(int left) { return left + 108; }
    public static int Round108(int left) { return left + 109; }
    public static int Round109(int left) { return left + 110; }
    public static int Round110(int left) { return left + 111; }
    public static int Round111(int left) { return left + 112; }
    public static int Round112(int left) { return left + 113; }
    public static int Round113(int left) { return left + 114; }
    public static int Round114(int left) { return left + 115; }
    public static int Round115(int left) { return left + 116; }
    public static int Round116(int left) { return left + 117; }
    public static int Round117(int left) { return left + 118; }
    public static int Round118(int left) { return left + 119; }
    public static int Round119(int left) { return left + 120; }
    public static int Round120(int left) { return left + 121; }
    public static int Round121(int left) { return left + 122; }
    public static int Round122(int left) { return left + 123; }
    public static int Round123(int left) { return left + 124; }
    public static int Round124(int left) { return left + 125; }
    public static int Round125(int left) { return left + 126; }
    public static int Round126(int left) { return left + 127; }
    public static int Round127(int left) { return left + 128; }
    public static int Round128(int left) { return left + 129; }
    public static int Round129(int left) { return left + 130; }
    public static int Round130(int left) { return left + 131; }
    public static int Round131(int left) { return left + 132; }
    public static int Round132(int left) { return left + 133; }
    public static int Round133(int left) { return left + 134; }
    public static int Round134(int left) { return left + 135; }
    public static int Round135(int left) { return left + 136; }
    public static int Round136(int left) { return left + 137; }
    public static int Round137(int left) { return left + 138; }
    public static int Round138(int left) { return left + 139; }
    public static int Round139(int left) { return left + 140; }
    public static int Round140(int left) { return left + 141; }
    public static int Round141(int left) { return left + 142; }
    public static int Round142(int left) { return left + 143; }
    public static int Round143(int left) { return left + 144; }
    public static int Round144(int left) { return left + 145; }
    public static int Round145(int left) { return left + 146; }
    public static int Round146(int left) { return left + 147; }
    public static int Round147(int left) { return left + 148; }
    public static int Round148(int left) { return left + 149; }
    public static int Round149(int left) { return left + 150; }
    public static int Round150(int left) { return left + 151; }
    public static int Round151(int left) { return left + 152; }
    public static int Round152(int left) { return left + 153; }
    public static int Round153(int left) { return left + 154; }
    public static int Round154(int left) { return left + 155; }
    public static int Round155(int left) { return left + 156; }
    public static int Round156(int left) { return left + 157; }
    public static int Round157(int left) { return left + 158; }
    public static int Round158(int left) { return left + 159; }
    public static int Round159(int left) { return left + 160; }
    public static int Round160(int left) { return left + 161; }
    public static int Round161(int left) { return left + 162; }
    public static int Round162(int left) { return left + 163; }
    public static int Round163(int left) { return left + 164; }
    public static int Round164(int left) { return left + 165; }
    public static int Round165(int left) { return left + 166; }
    public static int Round166(int left) { return left + 167; }
    public static int Round167(int left) { return left + 168; }
    public static int Round168(int left) { return left + 169; }
    public static int Round169(int left) { return left + 170; }
    public static int Round170(int left) { return left + 171; }
    public static int Round171(int left) { return left + 172; }
    public static int Round172(int left) { return left + 173; }
    public static int Round173(int left) { return left + 174; }
    public static int Round174(int left) { return left + 175; }
    public static int Round175(int left) { return left + 176; }
    public static int Round176(int left) { return left + 177; }
    public static int Round177(int left) { return left + 178; }
    public static int Round178(int left) { return left + 179; }
    public static int Round179(int left) { return left + 180; }
    public static int Round180(int left) { return left + 181; }
    public static int Round181(int left) { return left + 182; }
    public static int Round182(int left) { return left + 183; }
    public static int Round183(int left) { return left + 184; }
    public static int Round184(int left) { return left + 185; }
    public static int Round185(int left) { return left + 186; }
    public static int Round186(int left) { return left + 187; }
    public static int Round187(int left) { return left + 188; }
    public static int Round188(int left) { return left + 189; }
    public static int Round189(int left) { return left + 190; }
    public static int Round190(int left) { return left + 191; }
    public static int Round191(int left) { return left + 192; }
    public static int Round192(int left) { return left + 193; }
    public static int Round193(int left) { return left + 194; }
    public static int Round194(int left) { return left + 195; }
    public static int Round195(int left) { return left + 196; }
    public static int Round196(int left) { return left + 197; }
    public static int Round197(int left) { return left + 198; }
    public static int Round198(int left) { return left + 199; }
    public static int Round199(int left) { return left + 200; }
    public static int Round200(int left) { return left + 201; }
    public static int Round201(int left) { return left + 202; }
    public static int Round202(int left) { return left + 203; }
    public static int Round203(int left) { return left + 204; }
    public static int Round204(int left) { return left + 205; }
    public static int Round205(int left) { return left + 206; }
    public static int Round206(int left) { return left + 207; }
    public static int Round207(int left) { return left + 208; }
    public static int Round208(int left) { return left + 209; }
    public static int Round209(int left) { return left + 210; }
    public static int Round210(int left) { return left + 211; }
    public static int Round211(int left) { return left + 212; }
    public static int Round212(int left) { return left + 213; }
    public static int Round213(int left) { return left + 214; }
    public static int Round214(int left) { return left + 215; }
    public static int Round215(int left) { return left + 216; }
    public static int Round216(int left) { return left + 217; }
    public static int Round217(int left) { return left + 218; }
    public static int Round218(int left) { return left + 219; }
    public static int Round219(int left) { return left + 220; }
    public static int Round220(int left) { return left + 221; }
    public static int Round221(int left) { return left + 222; }
    public static int Round222(int left) { return left + 223; }
    public static int Round223(int left) { return left + 224; }
    public static int Round224(int left) { return left + 225; }
    public static int Round225(int left) { return left + 226; }
    public static int Round226(int left) { return left + 227; }
    public static int Round227(int left) { return left + 228; }
    public static int Round228(int left) { return left + 229; }
    public static int Round229(int left) { return left + 230; }
    public static int Round230(int left) { return left + 231; }
    public static int Round231(int left) { return left + 232; }
    public static int Round232(int left) { return left + 233; }
    public static int Round233(int left) { return left + 234; }
    public static int Round234(int left) { return left + 235; }
    public static int Round235(int left) { return left + 236; }
    public static int Round236(int left) { return left + 237; }
    public static int Round237(int left) { return left + 238; }
    public static int Round238(int left) { return left + 239; }
    public static int Round239(int left) { return left + 240; }
    public static int Round240(int left) { return left + 241; }
    public static int Round241(int left) { return left + 242; }
    public static int Round242(int left) { return left + 243; }
    public static int Round243(int left) { return left + 244; }
    public static int Round244(int left) { return left + 245; }
    public static int Round245(int left) { return left + 246; }
    public static int Round246(int left) { return left + 247; }
    public static int Round247(int left) { return left + 248; }
    public static int Round248(int left) { return left + 249; }
    public static int Round249(int left) { return left + 250; }
    public static int Round250(int left) { return left + 251; }
    public static int Round251(int left) { return left + 252; }
    public static int Round252(int left) { return left + 253; }
    public static int Round253(int left) { return left + 254; }
    public static int Round254(int left) { return left + 255; }
    public static int Round255(int left) { return left + 256; }
    public static int Round256(int left) { return left + 257; }
    public static int Round257(int left) { return left + 258; }
    public static int Round258(int left) { return left + 259; }
    public static int Round259(int left) { return left + 260; }
    public static int Round260(int left) { return left + 261; }
    public static int Round261(int left) { return left + 262; }
    public static int Round262(int left) { return left + 263; }
    public static int Round263(int left) { return left + 264; }
    public static int Round264(int left) { return left + 265; }
    public static int Round265(int left) { return left + 266; }
    public static int Round266(int left) { return left + 267; }
    public static int Round267(int left) { return left + 268; }
    public static int Round268(int left) { return left + 269; }
    public static int Round269(int left) { return left + 270; }
    public static int Round270(int left) { return left + 271; }
    public static int Round271(int left) { return left + 272; }
    public static int Round272(int left) { return left + 273; }
    public static int Round273(int left) { return left + 274; }
    public static int Round274(int left) { return left + 275; }
    public static int Round275(int left) { return left + 276; }
    public static int Round276(int left) { return left + 277; }
    public static int Round277(int left) { return left + 278; }
    public static int Round278(int left) { return left + 279; }
    public static int Round279(int left) { return left + 280; }
    public static int Round280(int left) { return left + 281; }
    public static int Round281(int left) { return left + 282; }
    public static int Round282(int left) { return left + 283; }
    public static int Round283(int left) { return left + 284; }
    public static int Round284(int left) { return left + 285; }
    public static int Round285(int left) { return left + 286; }
    public static int Round286(int left) { return left + 287; }
    public static int Round287(int left) { return left + 288; }
    public static int Round288(int left) { return left + 289; }
    public static int Round289(int left) { return left + 290; }
    public static int Round290(int left) { return left + 291; }
    public static int Round291(int left) { return left + 292; }
    public static int Round292(int left) { return left + 293; }
    public static int Round293(int left) { return left + 294; }
    public static int Round294(int left) { return left + 295; }
    public static int Round295(int left) { return left + 296; }
    public static int Round296(int left) { return left + 297; }
    public static int Round297(int left) { return left + 298; }
    public static int Round298(int left) { return left + 299; }
    public static int Round299(int left) { return left + 300; }
}

public static class ClaimRaceSubjects {
    public const int Count = 300;

    private static readonly MethodInfo[] Pool = Harvest();

    public static MethodInfo At(int round) {
        return Pool[round];
    }

    private static MethodInfo[] Harvest() {
        MethodInfo[] found = typeof(ClaimRaceSubjects)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("Round", StringComparison.Ordinal))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        if (found.Length != Count) {
            throw new InvalidOperationException(
                $"ClaimRaceSubjects holds {found.Length} fresh targets and {Count} were declared. These targets " +
                "stay separate from RaceSubjects and RevertRaceSubjects on purpose: a target shared between two " +
                "races carries the other race's forwarder, claim and foreign-patcher state, so one race's outcome " +
                "would depend on which test ran first.");
        }

        return found;
    }

    public static int Round000(int left) { return left + 1; }
    public static int Round001(int left) { return left + 2; }
    public static int Round002(int left) { return left + 3; }
    public static int Round003(int left) { return left + 4; }
    public static int Round004(int left) { return left + 5; }
    public static int Round005(int left) { return left + 6; }
    public static int Round006(int left) { return left + 7; }
    public static int Round007(int left) { return left + 8; }
    public static int Round008(int left) { return left + 9; }
    public static int Round009(int left) { return left + 10; }
    public static int Round010(int left) { return left + 11; }
    public static int Round011(int left) { return left + 12; }
    public static int Round012(int left) { return left + 13; }
    public static int Round013(int left) { return left + 14; }
    public static int Round014(int left) { return left + 15; }
    public static int Round015(int left) { return left + 16; }
    public static int Round016(int left) { return left + 17; }
    public static int Round017(int left) { return left + 18; }
    public static int Round018(int left) { return left + 19; }
    public static int Round019(int left) { return left + 20; }
    public static int Round020(int left) { return left + 21; }
    public static int Round021(int left) { return left + 22; }
    public static int Round022(int left) { return left + 23; }
    public static int Round023(int left) { return left + 24; }
    public static int Round024(int left) { return left + 25; }
    public static int Round025(int left) { return left + 26; }
    public static int Round026(int left) { return left + 27; }
    public static int Round027(int left) { return left + 28; }
    public static int Round028(int left) { return left + 29; }
    public static int Round029(int left) { return left + 30; }
    public static int Round030(int left) { return left + 31; }
    public static int Round031(int left) { return left + 32; }
    public static int Round032(int left) { return left + 33; }
    public static int Round033(int left) { return left + 34; }
    public static int Round034(int left) { return left + 35; }
    public static int Round035(int left) { return left + 36; }
    public static int Round036(int left) { return left + 37; }
    public static int Round037(int left) { return left + 38; }
    public static int Round038(int left) { return left + 39; }
    public static int Round039(int left) { return left + 40; }
    public static int Round040(int left) { return left + 41; }
    public static int Round041(int left) { return left + 42; }
    public static int Round042(int left) { return left + 43; }
    public static int Round043(int left) { return left + 44; }
    public static int Round044(int left) { return left + 45; }
    public static int Round045(int left) { return left + 46; }
    public static int Round046(int left) { return left + 47; }
    public static int Round047(int left) { return left + 48; }
    public static int Round048(int left) { return left + 49; }
    public static int Round049(int left) { return left + 50; }
    public static int Round050(int left) { return left + 51; }
    public static int Round051(int left) { return left + 52; }
    public static int Round052(int left) { return left + 53; }
    public static int Round053(int left) { return left + 54; }
    public static int Round054(int left) { return left + 55; }
    public static int Round055(int left) { return left + 56; }
    public static int Round056(int left) { return left + 57; }
    public static int Round057(int left) { return left + 58; }
    public static int Round058(int left) { return left + 59; }
    public static int Round059(int left) { return left + 60; }
    public static int Round060(int left) { return left + 61; }
    public static int Round061(int left) { return left + 62; }
    public static int Round062(int left) { return left + 63; }
    public static int Round063(int left) { return left + 64; }
    public static int Round064(int left) { return left + 65; }
    public static int Round065(int left) { return left + 66; }
    public static int Round066(int left) { return left + 67; }
    public static int Round067(int left) { return left + 68; }
    public static int Round068(int left) { return left + 69; }
    public static int Round069(int left) { return left + 70; }
    public static int Round070(int left) { return left + 71; }
    public static int Round071(int left) { return left + 72; }
    public static int Round072(int left) { return left + 73; }
    public static int Round073(int left) { return left + 74; }
    public static int Round074(int left) { return left + 75; }
    public static int Round075(int left) { return left + 76; }
    public static int Round076(int left) { return left + 77; }
    public static int Round077(int left) { return left + 78; }
    public static int Round078(int left) { return left + 79; }
    public static int Round079(int left) { return left + 80; }
    public static int Round080(int left) { return left + 81; }
    public static int Round081(int left) { return left + 82; }
    public static int Round082(int left) { return left + 83; }
    public static int Round083(int left) { return left + 84; }
    public static int Round084(int left) { return left + 85; }
    public static int Round085(int left) { return left + 86; }
    public static int Round086(int left) { return left + 87; }
    public static int Round087(int left) { return left + 88; }
    public static int Round088(int left) { return left + 89; }
    public static int Round089(int left) { return left + 90; }
    public static int Round090(int left) { return left + 91; }
    public static int Round091(int left) { return left + 92; }
    public static int Round092(int left) { return left + 93; }
    public static int Round093(int left) { return left + 94; }
    public static int Round094(int left) { return left + 95; }
    public static int Round095(int left) { return left + 96; }
    public static int Round096(int left) { return left + 97; }
    public static int Round097(int left) { return left + 98; }
    public static int Round098(int left) { return left + 99; }
    public static int Round099(int left) { return left + 100; }
    public static int Round100(int left) { return left + 101; }
    public static int Round101(int left) { return left + 102; }
    public static int Round102(int left) { return left + 103; }
    public static int Round103(int left) { return left + 104; }
    public static int Round104(int left) { return left + 105; }
    public static int Round105(int left) { return left + 106; }
    public static int Round106(int left) { return left + 107; }
    public static int Round107(int left) { return left + 108; }
    public static int Round108(int left) { return left + 109; }
    public static int Round109(int left) { return left + 110; }
    public static int Round110(int left) { return left + 111; }
    public static int Round111(int left) { return left + 112; }
    public static int Round112(int left) { return left + 113; }
    public static int Round113(int left) { return left + 114; }
    public static int Round114(int left) { return left + 115; }
    public static int Round115(int left) { return left + 116; }
    public static int Round116(int left) { return left + 117; }
    public static int Round117(int left) { return left + 118; }
    public static int Round118(int left) { return left + 119; }
    public static int Round119(int left) { return left + 120; }
    public static int Round120(int left) { return left + 121; }
    public static int Round121(int left) { return left + 122; }
    public static int Round122(int left) { return left + 123; }
    public static int Round123(int left) { return left + 124; }
    public static int Round124(int left) { return left + 125; }
    public static int Round125(int left) { return left + 126; }
    public static int Round126(int left) { return left + 127; }
    public static int Round127(int left) { return left + 128; }
    public static int Round128(int left) { return left + 129; }
    public static int Round129(int left) { return left + 130; }
    public static int Round130(int left) { return left + 131; }
    public static int Round131(int left) { return left + 132; }
    public static int Round132(int left) { return left + 133; }
    public static int Round133(int left) { return left + 134; }
    public static int Round134(int left) { return left + 135; }
    public static int Round135(int left) { return left + 136; }
    public static int Round136(int left) { return left + 137; }
    public static int Round137(int left) { return left + 138; }
    public static int Round138(int left) { return left + 139; }
    public static int Round139(int left) { return left + 140; }
    public static int Round140(int left) { return left + 141; }
    public static int Round141(int left) { return left + 142; }
    public static int Round142(int left) { return left + 143; }
    public static int Round143(int left) { return left + 144; }
    public static int Round144(int left) { return left + 145; }
    public static int Round145(int left) { return left + 146; }
    public static int Round146(int left) { return left + 147; }
    public static int Round147(int left) { return left + 148; }
    public static int Round148(int left) { return left + 149; }
    public static int Round149(int left) { return left + 150; }
    public static int Round150(int left) { return left + 151; }
    public static int Round151(int left) { return left + 152; }
    public static int Round152(int left) { return left + 153; }
    public static int Round153(int left) { return left + 154; }
    public static int Round154(int left) { return left + 155; }
    public static int Round155(int left) { return left + 156; }
    public static int Round156(int left) { return left + 157; }
    public static int Round157(int left) { return left + 158; }
    public static int Round158(int left) { return left + 159; }
    public static int Round159(int left) { return left + 160; }
    public static int Round160(int left) { return left + 161; }
    public static int Round161(int left) { return left + 162; }
    public static int Round162(int left) { return left + 163; }
    public static int Round163(int left) { return left + 164; }
    public static int Round164(int left) { return left + 165; }
    public static int Round165(int left) { return left + 166; }
    public static int Round166(int left) { return left + 167; }
    public static int Round167(int left) { return left + 168; }
    public static int Round168(int left) { return left + 169; }
    public static int Round169(int left) { return left + 170; }
    public static int Round170(int left) { return left + 171; }
    public static int Round171(int left) { return left + 172; }
    public static int Round172(int left) { return left + 173; }
    public static int Round173(int left) { return left + 174; }
    public static int Round174(int left) { return left + 175; }
    public static int Round175(int left) { return left + 176; }
    public static int Round176(int left) { return left + 177; }
    public static int Round177(int left) { return left + 178; }
    public static int Round178(int left) { return left + 179; }
    public static int Round179(int left) { return left + 180; }
    public static int Round180(int left) { return left + 181; }
    public static int Round181(int left) { return left + 182; }
    public static int Round182(int left) { return left + 183; }
    public static int Round183(int left) { return left + 184; }
    public static int Round184(int left) { return left + 185; }
    public static int Round185(int left) { return left + 186; }
    public static int Round186(int left) { return left + 187; }
    public static int Round187(int left) { return left + 188; }
    public static int Round188(int left) { return left + 189; }
    public static int Round189(int left) { return left + 190; }
    public static int Round190(int left) { return left + 191; }
    public static int Round191(int left) { return left + 192; }
    public static int Round192(int left) { return left + 193; }
    public static int Round193(int left) { return left + 194; }
    public static int Round194(int left) { return left + 195; }
    public static int Round195(int left) { return left + 196; }
    public static int Round196(int left) { return left + 197; }
    public static int Round197(int left) { return left + 198; }
    public static int Round198(int left) { return left + 199; }
    public static int Round199(int left) { return left + 200; }
    public static int Round200(int left) { return left + 201; }
    public static int Round201(int left) { return left + 202; }
    public static int Round202(int left) { return left + 203; }
    public static int Round203(int left) { return left + 204; }
    public static int Round204(int left) { return left + 205; }
    public static int Round205(int left) { return left + 206; }
    public static int Round206(int left) { return left + 207; }
    public static int Round207(int left) { return left + 208; }
    public static int Round208(int left) { return left + 209; }
    public static int Round209(int left) { return left + 210; }
    public static int Round210(int left) { return left + 211; }
    public static int Round211(int left) { return left + 212; }
    public static int Round212(int left) { return left + 213; }
    public static int Round213(int left) { return left + 214; }
    public static int Round214(int left) { return left + 215; }
    public static int Round215(int left) { return left + 216; }
    public static int Round216(int left) { return left + 217; }
    public static int Round217(int left) { return left + 218; }
    public static int Round218(int left) { return left + 219; }
    public static int Round219(int left) { return left + 220; }
    public static int Round220(int left) { return left + 221; }
    public static int Round221(int left) { return left + 222; }
    public static int Round222(int left) { return left + 223; }
    public static int Round223(int left) { return left + 224; }
    public static int Round224(int left) { return left + 225; }
    public static int Round225(int left) { return left + 226; }
    public static int Round226(int left) { return left + 227; }
    public static int Round227(int left) { return left + 228; }
    public static int Round228(int left) { return left + 229; }
    public static int Round229(int left) { return left + 230; }
    public static int Round230(int left) { return left + 231; }
    public static int Round231(int left) { return left + 232; }
    public static int Round232(int left) { return left + 233; }
    public static int Round233(int left) { return left + 234; }
    public static int Round234(int left) { return left + 235; }
    public static int Round235(int left) { return left + 236; }
    public static int Round236(int left) { return left + 237; }
    public static int Round237(int left) { return left + 238; }
    public static int Round238(int left) { return left + 239; }
    public static int Round239(int left) { return left + 240; }
    public static int Round240(int left) { return left + 241; }
    public static int Round241(int left) { return left + 242; }
    public static int Round242(int left) { return left + 243; }
    public static int Round243(int left) { return left + 244; }
    public static int Round244(int left) { return left + 245; }
    public static int Round245(int left) { return left + 246; }
    public static int Round246(int left) { return left + 247; }
    public static int Round247(int left) { return left + 248; }
    public static int Round248(int left) { return left + 249; }
    public static int Round249(int left) { return left + 250; }
    public static int Round250(int left) { return left + 251; }
    public static int Round251(int left) { return left + 252; }
    public static int Round252(int left) { return left + 253; }
    public static int Round253(int left) { return left + 254; }
    public static int Round254(int left) { return left + 255; }
    public static int Round255(int left) { return left + 256; }
    public static int Round256(int left) { return left + 257; }
    public static int Round257(int left) { return left + 258; }
    public static int Round258(int left) { return left + 259; }
    public static int Round259(int left) { return left + 260; }
    public static int Round260(int left) { return left + 261; }
    public static int Round261(int left) { return left + 262; }
    public static int Round262(int left) { return left + 263; }
    public static int Round263(int left) { return left + 264; }
    public static int Round264(int left) { return left + 265; }
    public static int Round265(int left) { return left + 266; }
    public static int Round266(int left) { return left + 267; }
    public static int Round267(int left) { return left + 268; }
    public static int Round268(int left) { return left + 269; }
    public static int Round269(int left) { return left + 270; }
    public static int Round270(int left) { return left + 271; }
    public static int Round271(int left) { return left + 272; }
    public static int Round272(int left) { return left + 273; }
    public static int Round273(int left) { return left + 274; }
    public static int Round274(int left) { return left + 275; }
    public static int Round275(int left) { return left + 276; }
    public static int Round276(int left) { return left + 277; }
    public static int Round277(int left) { return left + 278; }
    public static int Round278(int left) { return left + 279; }
    public static int Round279(int left) { return left + 280; }
    public static int Round280(int left) { return left + 281; }
    public static int Round281(int left) { return left + 282; }
    public static int Round282(int left) { return left + 283; }
    public static int Round283(int left) { return left + 284; }
    public static int Round284(int left) { return left + 285; }
    public static int Round285(int left) { return left + 286; }
    public static int Round286(int left) { return left + 287; }
    public static int Round287(int left) { return left + 288; }
    public static int Round288(int left) { return left + 289; }
    public static int Round289(int left) { return left + 290; }
    public static int Round290(int left) { return left + 291; }
    public static int Round291(int left) { return left + 292; }
    public static int Round292(int left) { return left + 293; }
    public static int Round293(int left) { return left + 294; }
    public static int Round294(int left) { return left + 295; }
    public static int Round295(int left) { return left + 296; }
    public static int Round296(int left) { return left + 297; }
    public static int Round297(int left) { return left + 298; }
    public static int Round298(int left) { return left + 299; }
    public static int Round299(int left) { return left + 300; }
}

public static class HoldingUpdater {
    public static readonly ConcurrentQueue<MethodBase> Writes = new ConcurrentQueue<MethodBase>();

    public static readonly ConcurrentQueue<MethodBase> Reverts = new ConcurrentQueue<MethodBase>();

    public static readonly ManualResetEventSlim Reached = new ManualResetEventSlim(false);

    private static int reached;

    private static int holding;

    public static Thread? Held { get; set; }

    public static int Hold { get; set; }

    public static bool Fired => Volatile.Read(ref reached) == 1;

    public static bool Holding => Volatile.Read(ref holding) == 1;

    public static void ReplaceMethodBody(MethodBase method, ReadOnlySpan<byte> methodBody, ReadOnlySpan<FakeUpdater.Entry> ilOffsetMap) {
        Writes.Enqueue(method);
    }

    public static void Revert(MethodBase method) {
        Reverts.Enqueue(method);
        if (!ReferenceEquals(Held, Thread.CurrentThread) || Interlocked.Exchange(ref reached, 1) != 0) {
            return;
        }

        Volatile.Write(ref holding, 1);
        Reached.Set();
        Thread.Sleep(Hold);
        Volatile.Write(ref holding, 0);
    }

    public static void Reset() {
        Writes.Clear();
        Reverts.Clear();
        Reached.Reset();
        Volatile.Write(ref reached, 0);
        Volatile.Write(ref holding, 0);
        Held = null;
    }
}

public sealed class RecordingBackend : IDetourBackend {
    public List<IReadOnlyList<Injection>> Composed { get; } = [];

    public List<RecordingHandle> Handles { get; } = [];

    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        RecordingHandle handle = new RecordingHandle();
        Handles.Add(handle);
        return handle;
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        Composed.Add(added);
        RecordingHandle handle = new RecordingHandle();
        Handles.Add(handle);
        return handle;
    }
}

public sealed class GateBackend : IDetourBackend {
    private int entries;

    public ManualResetEventSlim Entered { get; } = new ManualResetEventSlim(false);

    public ManualResetEventSlim Released { get; } = new ManualResetEventSlim(false);

    public ConcurrentQueue<IReadOnlyList<Injection>> Composed { get; } = new ConcurrentQueue<IReadOnlyList<Injection>>();

    public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
        if (Interlocked.Increment(ref entries) == 1) {
            Entered.Set();
            Released.Wait(TimeSpan.FromSeconds(30));
        }

        return new RecordingHandle();
    }

    public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
        Composed.Enqueue(added);
        return new RecordingHandle();
    }
}

public sealed class PublishSeam : IReadOnlyList<Injection>, IDisposable {
    private readonly IReadOnlyList<Injection> added;

    private readonly int hold;

    private int reached;

    public PublishSeam(IReadOnlyList<Injection> added, int hold) {
        this.added = added;
        this.hold = hold;
    }

    public ManualResetEventSlim Reached { get; } = new ManualResetEventSlim(false);

    public bool Fired => Volatile.Read(ref reached) == 1;

    public int Count {
        get {
            if (Interlocked.Exchange(ref reached, 1) == 0) {
                Reached.Set();
                Thread.Sleep(hold);
            }

            return added.Count;
        }
    }

    public Injection this[int index] => added[index];

    public void Dispose() {
        Reached.Dispose();
    }

    public IEnumerator<Injection> GetEnumerator() {
        return added.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
}

public sealed class ReleaseSeam : IEqualityComparer<MethodBase>, IDisposable {
    private readonly int hold;

    private int reached;

    public ReleaseSeam(int hold) {
        this.hold = hold;
    }

    public ManualResetEventSlim Reached { get; } = new ManualResetEventSlim(false);

    public Thread? Held { get; set; }

    public bool Fired => Volatile.Read(ref reached) == 1;

    public bool Equals(MethodBase? left, MethodBase? right) {
        return left is null ? right is null : left.Equals(right);
    }

    public int GetHashCode(MethodBase value) {
        if (ReferenceEquals(Held, Thread.CurrentThread) && Interlocked.Exchange(ref reached, 1) == 0) {
            Reached.Set();
            Thread.Sleep(hold);
        }

        return value.GetHashCode();
    }

    public void Dispose() {
        Reached.Dispose();
    }
}

public sealed class RecordingHandle : IDetourHandle {
    public MethodBase Original => typeof(RecordingHandle).GetMethod(nameof(Apply))!;

    public bool IsApplied => !Disposed;

    public bool Disposed { get; private set; }

    public static void Apply() {
    }

    public void Dispose() {
        Disposed = true;
    }
}

[Collection("AdapterStatics")]
public class WaylithDetourBackendTests : IDisposable {
    private const int SeamHoldMilliseconds = 1;

    private static readonly MethodInfo TwoArgs = typeof(WaylithDetourBackendTests).GetMethod(nameof(Sum))!;

    private static readonly MethodInfo TwoArgsWrong = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumWrong))!;

    private static readonly MethodInfo Spanned = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Spanned))!;

    private readonly WriterRegistry registry;

    private readonly WriterRegistry previousRegistry;

    private readonly ConcurrentDictionary<MethodBase, string> processClaims = new ConcurrentDictionary<MethodBase, string>();

    private readonly List<string> registryLog = [];

    private readonly string? previousEmptyMapVariable =
        Environment.GetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP");

    public WaylithDetourBackendTests() {
        previousRegistry = WaylithDetourBackend.Writers;
        registry = new WriterRegistry(registryLog.Add, processClaims);
        WaylithDetourBackend.Writers = registry;
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", null);
    }

    public void Dispose() {
        WaylithDetourBackend.EmptyMapAlways = false;
        WaylithDetourBackend.Writers = previousRegistry;
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", previousEmptyMapVariable);
        GC.SuppressFinalize(this);
    }

    public static int Sum(int left, int right) {
        return left + right;
    }

    public static long SumWrong(int left, int right) {
        return left + right;
    }

    public static int SumOneAgain(int left) {
        return left + 1;
    }

    public static int SumOne(int left) {
        return left;
    }

    public static int ShapedMissing(Shaped shaped, int left) {
        return left;
    }

    public static int CrateGet(Crate<int> crate, int n) {
        return n;
    }

    public static int SumFloats(float left, float right) {
        return (int)(left + right);
    }

    public static int ByRef(ref int left, out int right) {
        right = left;
        return left;
    }

    public static unsafe int Pointer(int* at) {
        return *at;
    }

    public static int Receiver(Holder holder, int value) {
        return value;
    }

    public static int StructReceiver(ref Counted counted, int value) {
        return value;
    }

    [Fact]
    public void TwoInjectedInstructionsInOneGapGetTwoDifferentReportedOffsets() {
        int original = Len(Spanned);
        ComposedBody body = new ComposedBody(
            [Copied(OpCodes.Nop, 0), Injected(OpCodes.Nop), Injected(OpCodes.Nop), Copied(OpCodes.Ret, 1)],
            [],
            [],
            false);
        EncodedBody encoded = new EncodedBody([], [0, 1, 2, 3], 1, 4);

        IReadOnlyList<OffsetPair> map = WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha")]);

        int first = Assert.Single(map, pair => pair.Replaced == 1).Original;
        int second = Assert.Single(map, pair => pair.Replaced == 2).Original;
        Assert.NotEqual(first, second);
        Assert.True(first >= original, $"the first injected instruction reported {first}, inside the {original}-byte original body");
        Assert.True(second >= original, $"the second injected instruction reported {second}, inside the {original}-byte original body");
    }

    [Fact]
    public void OffsetMapKeepsTargetOffsetsAndSentinelsEverythingElse() {
        int band = Len(Spanned);
        Assert.True(band > 9);
        ComposedBody body = new ComposedBody(
            [
                Copied(OpCodes.Nop, 5),
                Injected(OpCodes.Nop),
                Copied(OpCodes.Nop, 9),
                Rewritten(OpCodes.Ret),
            ],
            [],
            [],
            false);

        EncodedBody encoded = new EncodedBody([], [0, 1, 2, 3], 1, 4);
        IReadOnlyList<OffsetPair> map = WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha")]);

        Assert.Equal(
            [(5, 0), (band + 1, 1), (9, 2), (band + 3, 3)],
            map.Select(pair => (pair.Original, pair.Replaced)));
    }

    [Fact]
    public void NoSentinelEqualsARealTargetOffsetWhenTheTwoRangesAreAdjacent() {
        int band = Len(Spanned);
        ComposedInstruction[] stream = new ComposedInstruction[band + 1];
        int[] offsets = new int[band + 1];
        stream[0] = Injected(OpCodes.Nop);
        offsets[0] = 0;
        for (int i = 0; i < band; i++) {
            stream[i + 1] = Copied(OpCodes.Nop, i);
            offsets[i + 1] = i + 1;
        }

        EncodedBody encoded = new EncodedBody([], offsets, 1, band + 1);

        IReadOnlyList<OffsetPair> map = WaylithDetourBackend.OffsetMap(
            Spanned,
            new ComposedBody(stream, [], [], false),
            encoded,
            [Marker("alpha")]);

        Assert.Equal(band, map[0].Original);
        Assert.Equal(Enumerable.Range(0, band), map.Skip(1).Select(pair => pair.Original));
        Assert.Equal(map.Count, map.Select(pair => pair.Original).Distinct().Count());
    }

    [Fact]
    public void OffsetMapEmitsInBodyOrderAndCollapsesAdjacentRuns() {
        ComposedBody body = new ComposedBody(
            [Copied(OpCodes.Nop, 7), Copied(OpCodes.Nop, 2), Copied(OpCodes.Nop, 2), Copied(OpCodes.Ret, 7)],
            [],
            [],
            false);

        EncodedBody encoded = new EncodedBody([], [40, 10, 60, 90], 1, 4);
        IReadOnlyList<OffsetPair> map = WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha")]);

        Assert.Equal([(7, 40), (2, 10), (7, 90)], map.Select(pair => (pair.Original, pair.Replaced)));
    }

    [Fact]
    public void OffsetMapRefusesAnEncodedBodyWhoseOffsetCountDoesNotMatch() {
        ComposedBody body = new ComposedBody([Copied(OpCodes.Ret, 0)], [], [], false);
        EncodedBody encoded = new EncodedBody([], [0, 4], 1, 2);

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha")]));

        Assert.Contains("cannot be paired into an offset map", failure.Message);
    }

    [Fact]
    public void TheSwitchDropsEveryPairWhateverTheOwnerCountIs() {
        WaylithDetourBackend.EmptyMapAlways = true;
        ComposedBody body = new ComposedBody(
            [Copied(OpCodes.Nop, 5), Injected(OpCodes.Nop), Copied(OpCodes.Ret, 9)],
            [],
            [],
            false);
        EncodedBody encoded = new EncodedBody([], [0, 1, 2], 1, 3);

        Assert.Empty(WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha")]));
        Assert.Empty(WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha"), Second("alpha")]));
        Assert.Empty(WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha"), Second("beta")]));
    }

    [Fact]
    public void WithNothingInTheEnvironmentTwoOwnersKeepTheSentinelMap() {
        Assert.NotEmpty(TwoOwnerMap());
    }

    [Fact]
    public void TheEnvironmentAloneCanEmptyTheMapAtTwoOwnersFromOutsideTheProcess() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", "on");

        Assert.Empty(TwoOwnerMap());
    }

    [Fact]
    public void TheEnvironmentTurnedOffLeavesTheSentinelMapInPlace() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", "off");

        Assert.NotEmpty(TwoOwnerMap());
    }

    [Fact]
    public void AnUnreadableEnvironmentValueIsReportedAndThenLeavesTheSentinelMapInPlace() {
        AdapterEntryPoint.ResetForTests();
        Action<string> previous = WaylithDetourBackend.Log;
        List<string> logged = [];
        WaylithDetourBackend.Log = logged.Add;

        try {
            Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", "maybe");

            Assert.NotEmpty(TwoOwnerMap());
            Assert.Contains(logged, line => line.Contains("is set to 'maybe'", StringComparison.Ordinal));
        } finally {
            WaylithDetourBackend.Log = previous;
            AdapterEntryPoint.ResetForTests();
        }
    }

    [Fact]
    public void AnInProcessTrueSurvivesAnEnvironmentThatSaysOff() {
        Environment.SetEnvironmentVariable("CONCORD_WAYLITH_EMPTY_MAP", "off");
        WaylithDetourBackend.EmptyMapAlways = true;

        Assert.Empty(TwoOwnerMap());
    }

    [Fact]
    public void TheSwitchEmptiesAForwarderMapToo() {
        WaylithDetourBackend.EmptyMapAlways = true;
        FakeUpdater.Reset();
        List<string> logged = [];
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = logged.Add;
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Shelved))!;

        try {
            WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));
            Tokenized(() => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));
        } finally {
            WaylithDetourBackend.Log = previous;
        }

        Assert.Empty(FakeUpdater.Writes.Single().Map);
        string warning = Assert.Single(
            logged,
            line => line.Contains(WaylithDetourBackend.NoOffsetMapMarker, StringComparison.Ordinal));
        Assert.Contains(AdapterEntryPoint.EmptyMapVariable, warning, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoOwnersStillGetSentinelsSoInjectedCodeIsToldApartFromTheTargetsOwnIl() {
        int band = Len(Spanned);
        ComposedBody body = new ComposedBody(
            [
                Copied(OpCodes.Nop, 5),
                InjectedBy(OpCodes.Nop, nameof(PatchSubject.Marker)),
                InjectedBy(OpCodes.Nop, nameof(PatchSubject.SecondMarker)),
                Copied(OpCodes.Ret, 9),
            ],
            [],
            [],
            false);
        EncodedBody encoded = new EncodedBody([], [0, 1, 2, 3], 1, 4);

        IReadOnlyList<OffsetPair> map =
            WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha"), Second("beta")]);

        Assert.Equal(
            [(5, 0), (band + 1, 1), (band + 2, 2), (9, 3)],
            map.Select(pair => (pair.Original, pair.Replaced)));
        Assert.NotEqual(map[1].Original, map[2].Original);
        Assert.All(map.Skip(1).Take(2), pair => Assert.True(pair.Original >= band));
    }

    [Fact]
    public void ABodyWithNoOriginalIlIsAllSentinelAndStillDistinguishesItsInstructions() {
        int band = Len(Spanned);
        ComposedBody body = new ComposedBody([Injected(OpCodes.Nop), Injected(OpCodes.Ret)], [], [], false);
        EncodedBody encoded = new EncodedBody([], [0, 1], 1, 2);

        IReadOnlyList<OffsetPair> map = WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha")]);

        Assert.Equal([(band, 0), (band + 1, 1)], map.Select(pair => (pair.Original, pair.Replaced)));
    }

    [Fact]
    public void ABodyTooLargeForTheMeasuredSentinelRangeGetsNoMapAndSaysWhy() {
        ComposedBody body = new ComposedBody([Copied(OpCodes.Nop, 0), Injected(OpCodes.Ret)], [], [], false);
        EncodedBody encoded = new EncodedBody([], [0, 1], 1, WaylithDetourBackend.SentinelCeiling);

        Assert.Equal(-1, WaylithDetourBackend.SentinelBand(Spanned, encoded));
        Assert.Empty(WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha")]));
        Assert.Contains(
            "runs past the offset the runtime is known to carry",
            WaylithDetourBackend.NoOffsetMapWarning(TwoArgs, 1),
            StringComparison.Ordinal);
        Assert.Contains(
            "runs past the offset the runtime is known to carry",
            WaylithDetourBackend.NoOffsetMapWarning(TwoArgs, 2),
            StringComparison.Ordinal);

        WaylithDetourBackend.EmptyMapAlways = true;

        Assert.Contains(
            AdapterEntryPoint.EmptyMapVariable + " is on, so no map is written for any body",
            WaylithDetourBackend.NoOffsetMapWarning(TwoArgs, 2),
            StringComparison.Ordinal);
        Assert.Contains(
            AdapterEntryPoint.EmptyMapVariable + " is on, so no map is written for any body",
            WaylithDetourBackend.NoOffsetMapWarning(TwoArgs, 1),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheWholeSentinelBandStaysInsideTheRangeTheProbeMeasured() {
        EncodedBody largest = new EncodedBody([], [0], 1, WaylithDetourBackend.SentinelCeiling - Len(Spanned));
        int band = WaylithDetourBackend.SentinelBand(Spanned, largest);

        Assert.Equal(Len(Spanned), band);
        Assert.InRange((long)band + largest.CodeSize - 1, 0, WaylithDetourBackend.SentinelCeiling);
        Assert.Equal(-1, WaylithDetourBackend.SentinelBand(Spanned, new EncodedBody([], [0], 1, largest.CodeSize + 1)));
    }

    [Fact]
    public void EncodedOffsetsAreIlRelativeSoTheyCannotIndexTheBytes() {
        ComposedBody forwarder = WaylithDetourBackend.ForwarderBody(TwoArgs, TwoArgs);
        EncodedBody encoded = BodyEncoder.Encode(forwarder, new Tokens(), new ComputedStack());

        Assert.Equal(0, encoded.InstructionOffsets[0]);
        Assert.True(encoded.Bytes.Length > encoded.CodeSize);
    }

    [Fact]
    public void ForwarderLoadsEveryArgumentOfAStaticTargetThenCallsAndReturns() {
        ComposedBody forwarder = WaylithDetourBackend.ForwarderBody(TwoArgs, TwoArgs);

        Assert.Equal(
            [(OpCodes.Ldarg, (object?)0), (OpCodes.Ldarg, 1), (OpCodes.Call, TwoArgs), (OpCodes.Ret, null)],
            forwarder.Instructions.Select(step => (step.Instruction.opcode, step.Instruction.operand)));
        Assert.Empty(forwarder.Locals);
        Assert.Empty(forwarder.Clauses);
    }

    [Fact]
    public void ForwarderLoadsTheReceiverAheadOfTheDeclaredArgumentsOfAnInstanceTarget() {
        MethodInfo target = typeof(Holder).GetMethod(nameof(Holder.Read))!;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(Receiver))!;

        ComposedBody forwarder = WaylithDetourBackend.ForwarderBody(target, replacement);

        Assert.Equal([0, 1], forwarder.Instructions.Take(2).Select(step => step.Instruction.operand));
        Assert.Equal(4, forwarder.Instructions.Count);
        Assert.Null(WaylithDetourBackend.ForwarderRefusal(target, replacement));
    }

    [Fact]
    public void AValueTypeReceiverIsForwardedByReference() {
        MethodInfo target = typeof(Counted).GetMethod(nameof(Counted.Read))!;

        Assert.Null(WaylithDetourBackend.ForwarderRefusal(
            target,
            typeof(WaylithDetourBackendTests).GetMethod(nameof(StructReceiver))!));
        Assert.NotNull(WaylithDetourBackend.ForwarderRefusal(
            target,
            typeof(WaylithDetourBackendTests).GetMethod(nameof(Receiver))!));
    }

    [Fact]
    public void ByRefAndPointerParametersForwardUnchanged() {
        MethodInfo byref = typeof(WaylithDetourBackendTests).GetMethod(nameof(ByRef))!;
        MethodInfo pointer = typeof(WaylithDetourBackendTests).GetMethod(nameof(Pointer))!;

        Assert.Null(WaylithDetourBackend.ForwarderRefusal(byref, byref));
        Assert.Null(WaylithDetourBackend.ForwarderRefusal(pointer, pointer));
        Assert.Equal(4, WaylithDetourBackend.ForwarderBody(byref, byref).Instructions.Count);
        Assert.Equal(3, WaylithDetourBackend.ForwarderBody(pointer, pointer).Instructions.Count);
    }

    [Fact]
    public void ForwarderRefusesAReplacementWhoseReturnTypeDiffers() {
        Assert.Contains(
            "and the replacement returns",
            WaylithDetourBackend.ForwarderRefusal(TwoArgs, TwoArgsWrong));
    }

    [Fact]
    public void ForwarderRefusesAnInstanceReplacement() {
        MethodInfo instance = typeof(Holder).GetMethod(nameof(Holder.Read))!;

        Assert.Contains(
            "the replacement is an instance method",
            WaylithDetourBackend.ForwarderRefusal(instance, instance));
    }

    [Fact]
    public void ForwarderRefusesAReplacementWithTheWrongArgumentCount() {
        Assert.Contains(
            "A replaced body can neither drop an argument nor invent one",
            WaylithDetourBackend.ForwarderRefusal(TwoArgs, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));
    }

    [Fact]
    public void ForwarderRefusesAReplacementWithTheWrongArgumentType() {
        Assert.Contains(
            "on the target and",
            WaylithDetourBackend.ForwarderRefusal(TwoArgs, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumFloats))!));
    }

    [Fact]
    public void ADynamicReplacementCannotBeForwarded() {
        DynamicMethod generated = new DynamicMethod("generated", typeof(int), [typeof(int), typeof(int)]);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        Assert.Contains(
            "lives in a dynamic assembly",
            WaylithDetourBackend.UnforwardableShape(TwoArgs, generated.GetBaseDefinition()));
    }

    [Fact]
    public void AGenericTargetCannotBeForwarded() {
        Assert.Contains(
            "the target is generic",
            WaylithDetourBackend.UnforwardableShape(typeof(List<int>).GetMethod(nameof(List<int>.Clear))!, TwoArgs));
        Assert.Contains(
            "the target is generic",
            WaylithDetourBackend.UnforwardableShape(
                typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(typeof(int)),
                TwoArgs));
        Assert.Null(WaylithDetourBackend.UnforwardableShape(TwoArgs, TwoArgs));
    }

    [Fact]
    public void TheForwarderEncodesToLoadsThenACallThenAReturn() {
        ComposedBody forwarder = WaylithDetourBackend.ForwarderBody(TwoArgs, TwoArgs);
        Tokens tokens = new Tokens();
        EncodedBody encoded = BodyEncoder.Encode(forwarder, tokens, new ComputedStack());
        List<Step> steps = ReadBack.Steps(ReadBack.Body(encoded.Bytes).Il);

        Assert.Equal(
            [OpCodes.Ldarg, OpCodes.Ldarg, OpCodes.Call, OpCodes.Ret],
            steps.Select(step => step.OpCode));
        Assert.Equal([0L, 1L], steps.Take(2).Select(step => step.Operand));
        Assert.Equal(tokens.TokenFor(TwoArgs), (int)steps[2].Operand);
    }

    [Fact]
    public void TheWriterBindsAgainstAnUpdaterShapedLikeTheRuntimesAndWritesTheBody() {
        FakeUpdater.Reset();
        ForkBodyWriter writer = new ForkBodyWriter(typeof(FakeUpdater));
        Assert.True(writer.IsAvailable);

        writer.Write(TwoArgs, [1, 2, 3], [new OffsetPair(4, 40), new OffsetPair(5, 50)]);

        Assert.Equal(TwoArgs, FakeUpdater.Writes.Single().Target);
        Assert.Equal([1, 2, 3], FakeUpdater.Writes.Single().Body);
        Assert.Equal(
            [(4, 40), (5, 50)],
            FakeUpdater.Writes.Single().Map.Select(entry => (entry.OriginalOffset, entry.NewOffset)));
    }

    [Fact]
    public void TheWriterPassesAnEmptyMapThrough() {
        FakeUpdater.Reset();
        new ForkBodyWriter(typeof(FakeUpdater)).Write(TwoArgs, [1], []);

        Assert.Empty(FakeUpdater.Writes.Single().Map);
    }

    [Fact]
    public void TheWriterRevertsTheTargetItIsGiven() {
        FakeUpdater.Reset();
        new ForkBodyWriter(typeof(FakeUpdater)).Restore(TwoArgs);

        Assert.Equal(TwoArgs, FakeUpdater.Reverts.Single());
    }

    [Fact]
    public void TheWriterIsUnavailableWithoutTheRuntimeApiAndSaysSo() {
        ForkBodyWriter writer = new ForkBodyWriter(null);
        Assert.False(writer.IsAvailable);

        PlatformNotSupportedException failure = Assert.Throws<PlatformNotSupportedException>(
            () => writer.Write(TwoArgs, [1], []));

        Assert.Contains("ReplaceMethodBody", failure.Message);
        Assert.Contains("keep the detour backend", failure.Message);
    }

    [Fact]
    public void TheWriterIsUnavailableWhenTheUpdaterHasADifferentShape() {
        Assert.False(new ForkBodyWriter(typeof(WrongShapeUpdater)).IsAvailable);
    }

    [Fact]
    public void AHandedOffTargetComposesThroughTheDetourBackendInsteadOfABody() {
        FakeUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(detours, new ForkBodyWriter(typeof(FakeUpdater)));
        Injection[] ordered = [Dummy("first")];

        publisher.MarkHandedOff(TwoArgs);
        publisher.PublishComposed(TwoArgs, ordered);

        Assert.True(publisher.IsHandedOff(TwoArgs));
        Assert.Equal(ordered, detours.Composed.Single());
        Assert.Empty(FakeUpdater.Writes);
        Assert.Empty(FakeUpdater.Reverts);
    }

    [Fact]
    public void RepublishingAHandedOffTargetDropsThePreviousDetourFirst() {
        FakeUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(detours, new ForkBodyWriter(typeof(FakeUpdater)));

        publisher.MarkHandedOff(TwoArgs);
        publisher.PublishComposed(TwoArgs, [Dummy("first")]);
        publisher.PublishComposed(TwoArgs, [Dummy("first"), Dummy("second")]);

        Assert.True(detours.Handles[0].Disposed);
        Assert.False(detours.Handles[1].Disposed);
        Assert.Equal(2, detours.Composed[1].Count);
        Assert.Empty(FakeUpdater.Reverts);
    }

    [Fact]
    public void RevertingAHandedOffTargetDropsItsDetourAndLeavesTheBodyAlone() {
        FakeUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(detours, new ForkBodyWriter(typeof(FakeUpdater)));

        publisher.MarkHandedOff(TwoArgs);
        publisher.PublishComposed(TwoArgs, [Dummy("first")]);
        publisher.RevertToOriginal(TwoArgs);

        Assert.True(detours.Handles.Single().Disposed);
        Assert.Empty(FakeUpdater.Reverts);
        Assert.True(publisher.IsHandedOff(TwoArgs));
    }

    [Fact]
    public void RevertingAReplacedTargetRestoresTheOriginalBody() {
        FakeUpdater.Reset();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(
            new RecordingBackend(),
            new ForkBodyWriter(typeof(FakeUpdater)));
        registry.Claim(TwoArgs, [1], (IReadOnlyList<string>)[]);

        publisher.RevertToOriginal(TwoArgs);

        Assert.Equal(TwoArgs, FakeUpdater.Reverts.Single());
    }

    [Fact]
    public void ConstructingTheBackendTurnsComposedBodyCaptureOnBeforeAnythingComposes() {
        WrapperComposer.CaptureComposedBody = false;

        _ = new WaylithDetourBackend(
            new RecordingBackend(),
            new ComposedBodyPublisher(new RecordingBackend(), new ForkBodyWriter(typeof(FakeUpdater))),
            new ForkBodyWriter(typeof(FakeUpdater)));

        Assert.True(WrapperComposer.CaptureComposedBody);
    }

    [Fact]
    public void PublishingAPatchedTargetWritesAnEncodedBodyAndAnOffsetMapOfItsOwnIl() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Sum))!;
        int original = target.GetMethodBody()!.GetILAsByteArray()!.Length;

        Publish(target, typeof(FakeUpdater));

        (MethodBase Target, byte[] Body, FakeUpdater.Entry[] Map) written = FakeUpdater.Writes.Single();
        Decoded decoded = ReadBack.Body(written.Body);

        Assert.Equal(target, written.Target);
        Assert.True(decoded.CodeSize > 0);
        Assert.NotEmpty(written.Map);
        Assert.Contains(written.Map, entry => entry.OriginalOffset < original);
        Assert.Contains(written.Map, entry => entry.OriginalOffset >= original);
        Assert.All(
            written.Map,
            entry => Assert.InRange(
                entry.OriginalOffset,
                0,
                entry.OriginalOffset < original ? original - 1 : original + decoded.CodeSize - 1));
        Assert.All(written.Map, entry => Assert.InRange(entry.NewOffset, 0, decoded.CodeSize - 1));
        Assert.Equal(
            written.Map.OrderBy(entry => entry.NewOffset).Select(entry => entry.NewOffset),
            written.Map.Select(entry => entry.NewOffset));
    }

    [Fact]
    public void TheSwitchSendsABodyOutWithAnEmptyOffsetMapSoFramesKeepTheirReplacedOffsets() {
        WaylithDetourBackend.EmptyMapAlways = true;
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Surrendered))!;

        PublishOwned(target, [], Marker("alpha"), Second("beta"));

        Assert.Empty(FakeUpdater.Writes.Single().Map);
    }

    [Fact]
    public void AnEmptyOffsetMapIsAnnouncedAsUnreliableLineNumbersForThatMethod() {
        WaylithDetourBackend.EmptyMapAlways = true;
        FakeUpdater.Reset();
        List<string> logged = [];
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Seized))!;

        PublishOwned(target, logged, Marker("alpha"), Second("beta"));

        string warning = Assert.Single(
            logged,
            line => line.Contains(WaylithDetourBackend.NoOffsetMapMarker, StringComparison.Ordinal));
        Assert.Contains(nameof(PatchSubject.Seized), warning, StringComparison.Ordinal);
        Assert.Contains("unreliable", warning, StringComparison.Ordinal);
        Assert.Contains("owner of a frame", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void AFullOffsetMapIsNotAnnouncedAsUnreliable() {
        FakeUpdater.Reset();
        List<string> logged = [];
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Questioned))!;

        PublishOwned(target, logged, Marker("alpha"));

        Assert.NotEmpty(FakeUpdater.Writes.Single().Map);
        Assert.DoesNotContain(
            logged,
            line => line.Contains(WaylithDetourBackend.NoOffsetMapMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void TwoInjectionsFromOneOwnerKeepTheFullMap() {
        FakeUpdater.Reset();
        List<string> logged = [];
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Withheld))!;

        PublishOwned(target, logged, Marker("alpha"), Second("alpha"));

        Assert.NotEmpty(FakeUpdater.Writes.Single().Map);
        Assert.DoesNotContain(
            logged,
            line => line.Contains(WaylithDetourBackend.NoOffsetMapMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void AWriteTheRuntimeRefusesFallsBackToADetourAndSaysSoOnce() {
        FakeUpdater.Reset();
        List<string> logged = [];
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = logged.Add;
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Refused))!;

        try {
            RecordingBackend detours = Publish(target, typeof(ThrowingUpdater));

            Assert.Single(detours.Composed);
            Assert.Contains(TargetEligibility.FallbackMarker, logged.Single());
            Assert.Contains("this runtime refuses that method", logged.Single());
        } finally {
            WaylithDetourBackend.Log = previous;
        }
    }

    [Fact]
    public void AWriteTheRuntimeRefusesNeverReportsAFailedUndoForABodyItNeverWrote() {
        FakeUpdater.Reset();
        List<string> logged = [];
        Action<string> previous = WaylithDetourBackend.Log;
        WriterRegistry earlier = WaylithDetourBackend.Writers;
        WaylithDetourBackend.Log = logged.Add;
        WaylithDetourBackend.Writers = new WriterRegistry(static _ => { });
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Refused))!;

        try {
            Publish(target, typeof(RefusingUpdater));

            Assert.DoesNotContain(logged, line => line.Contains(WaylithTargetStack.UndoFailedMarker, StringComparison.Ordinal));
        } finally {
            WaylithDetourBackend.Log = previous;
            WaylithDetourBackend.Writers = earlier;
        }
    }

    [Fact]
    public void AWriteTheRuntimeRefusesCountsAsNoPublishedBodyAndNoPublishedBytes() {
        FakeUpdater.Reset();
        Action<string> previous = WaylithDetourBackend.Log;
        WriterRegistry earlier = WaylithDetourBackend.Writers;
        WriterRegistry registry = new WriterRegistry(static _ => { });
        WaylithDetourBackend.Log = static _ => { };
        WaylithDetourBackend.Writers = registry;
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Refused))!;

        try {
            Publish(target, typeof(RefusingUpdater));

            Assert.Equal(0, registry.PublishedBodies);
            Assert.Equal(0, registry.PublishedBytes);
        } finally {
            WaylithDetourBackend.Log = previous;
            WaylithDetourBackend.Writers = earlier;
        }
    }

    [Fact]
    public void ApplyInstallsAForwarderBodyAndDisposingItRevertsTheTarget() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Forwarded))!;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));

        IDetourHandle handle = Tokenized(() => backend.Apply(target, replacement));

        Assert.True(handle.IsApplied);
        Assert.Equal(target, FakeUpdater.Writes.Single().Target);
        Assert.Equal(
            [OpCodes.Ldarg, OpCodes.Call, OpCodes.Ret],
            ReadBack.Steps(ReadBack.Body(FakeUpdater.Writes.Single().Body).Il).Select(step => step.OpCode));

        handle.Dispose();

        Assert.False(handle.IsApplied);
        Assert.Equal(target, FakeUpdater.Reverts.Single());
    }

    [Fact]
    public void AForwarderKeepsNoneOfTheTargetsIlSoEveryEntryInItsMapIsASentinel() {
        FakeUpdater.Reset();
        List<string> logged = [];
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = logged.Add;
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Handed))!;

        try {
            WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));
            Tokenized(() => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));
        } finally {
            WaylithDetourBackend.Log = previous;
        }

        FakeUpdater.Entry[] map = FakeUpdater.Writes.Single().Map;
        Assert.NotEmpty(map);
        Assert.All(map, entry => Assert.True(entry.OriginalOffset >= Len(target)));
        Assert.Equal(map.Length, map.Select(entry => entry.OriginalOffset).Distinct().Count());
        Assert.DoesNotContain(
            logged,
            line => line.Contains(WaylithDetourBackend.NoOffsetMapMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void ComposingOverAForwarderIsRefusedBecauseAMethodHasOneBody() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Taken))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));
        Tokenized(() => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => backend.ApplyComposed(target, [Dummy("late")]));

        Assert.Contains("already carries a forwarder body", failure.Message);
    }

    [Fact]
    public void ForwardingOverAComposedBodyIsRefusedBecauseAMethodHasOneBody() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Claimed))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));
        IDetourHandle composed = Tokenized(() => backend.ApplyComposed(target, [Marker("early")]));

        Assert.Single(FakeUpdater.Writes);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => Tokenized(
                () => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!)));

        Assert.Contains("already carries a composed body", failure.Message);
        Assert.True(composed.IsApplied);
        Assert.Single(FakeUpdater.Writes);
    }

    [Fact]
    public void ASecondForwarderOverAForwarderIsRefusedInsteadOfOverwritingTheFirst() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Doubled))!;
        RecordingBackend detours = new RecordingBackend();
        WaylithDetourBackend backend = Backend(detours, typeof(FakeUpdater));
        IDetourHandle first = Tokenized(
            () => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));

        Exception? second = Record.Exception(
            () => Tokenized(
                () => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOneAgain))!)));

        Assert.True(first.IsApplied);
        Assert.Empty(detours.Handles);
        Assert.Single(FakeUpdater.Writes);
        InvalidOperationException refusal = Assert.IsType<InvalidOperationException>(second);
        Assert.Contains("already carries a forwarder body", refusal.Message);

        first.Dispose();
    }

    [Fact]
    public void AForwarderBodyIsCountedInThePublishRecordsLikeAComposedOneIs() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Tallied))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));

        IDetourHandle handle = Tokenized(
            () => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));

        Assert.True(handle.IsApplied);
        Assert.Equal(1, registry.PublishedBodies);
        Assert.Equal(FakeUpdater.Writes.Single().Body.Length, registry.PublishedBytes);

        handle.Dispose();
    }

    [Fact]
    public void AComposeThatFailsOnTheFirstApplyNeverRestoresABodyItDidNotWrite() {
        UntouchedRevertUpdater.Reverts.Clear();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Rolled))!;
        List<string> logged = [];
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = logged.Add;

        try {
            Assert.Same(registry, WaylithDetourBackend.Writers);
            Assert.True(
                registry.ClaimCount == 0,
                "a claim reached this test's own registry before the test ran anything: " + HeldClaims());
            WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(UntouchedRevertUpdater));

            Assert.Throws<ConcordEmitException>(
                () => Tokenized(() => backend.ApplyComposed(target, [LocalHead("test")])));
            Assert.True(
                UntouchedRevertUpdater.Reverts.Count == 0,
                "the rollback restored a body no publish ever wrote. " + ClaimState(target) + " Logged: " +
                string.Join(" | ", logged));
            Assert.False(registry.HoldsClaim(target));
        } finally {
            WaylithDetourBackend.Log = previous;
        }
    }

    private string ClaimState(MethodBase target) {
        WriterRegistry writers = WaylithDetourBackend.Writers;
        return "WaylithDetourBackend.Writers " + (ReferenceEquals(writers, registry) ? "is" : "is NOT") +
            " this test's registry, HoldsClaim reports " + writers.HoldsClaim(target) + ", and the claim on this " +
            "target is " + (writers.RecordFor(target)?.ToString() ?? "absent") + ". " + HeldClaims();
    }

    private string HeldClaims() {
        IReadOnlyList<WriterRecord> held = registry.Claims();
        return held.Count == 0
            ? "This test's registry holds no claim at all."
            : "This test's registry holds " + held.Count + " claim(s): " + string.Join(" / ", held) + ".";
    }

    [Fact]
    public void AForwarderIsAllowedOnceTheComposedBodyIsGone() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Released))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));
        Tokenized(() => backend.ApplyComposed(target, [Marker("early")])).Dispose();

        IDetourHandle forwarder = Tokenized(
            () => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));

        Assert.True(forwarder.IsApplied);
        Assert.Equal(2, FakeUpdater.Writes.Count);
    }

    [Fact]
    public void ApplyHandsADynamicReplacementToTheDetourBackend() {
        FakeUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Generated))!;
        DynamicMethod generated = new DynamicMethod("generated", typeof(int), [typeof(int)]);
        ILGenerator il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        IDetourHandle handle = Tokenized(() => Backend(detours, typeof(FakeUpdater)).Apply(target, generated));

        Assert.Same(detours.Handles.Single(), handle);
        Assert.Empty(FakeUpdater.Writes);
    }

    [Fact]
    public void AForwarderTheRuntimeRefusesFallsBackToADetour() {
        FakeUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Denied))!;

        IDetourHandle handle = Tokenized(
            () => Backend(detours, typeof(ThrowingUpdater)).Apply(
                target,
                typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));

        Assert.Same(detours.Handles.Single(), handle);
        Assert.False(registry.HoldsClaim(target));
    }

    [Fact]
    public void HarmonyArrivingAfterAPublishGivesTheBodyUpAndDropsItsBookkeeping() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Contended))!;
        ComposedBodyPublisher publisher = Publisher(new RecordingBackend(), typeof(FakeUpdater), target);
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };

        try {
            Assert.True(registry.HoldsClaim(target));
            ForeignPatcherPolicy.Owners = method => method == target ? ["harmony.somemod"] : null;

            Tokenized(() => {
                publisher.PublishComposed(target, [Marker("test"), Marker("second")]);
                return new RecordingHandle();
            });

            Assert.Single(FakeUpdater.Writes);
            Assert.False(registry.HoldsClaim(target));
            Assert.Empty(FrameAttribution.OwnersRegisteredFor(target));
        } finally {
            WaylithDetourBackend.Log = previous;
            ForeignPatcherPolicy.ResetForTests();
        }
    }

    [Fact]
    public void AGenericTargetIsHandedOffUnderTheKeyEveryInstantiationOfItsBodyShares() {
        FakeUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(detours, new ForkBodyWriter(typeof(FakeUpdater)));
        WaylithDetourBackend backend = new WaylithDetourBackend(
            detours,
            publisher,
            new ForkBodyWriter(typeof(FakeUpdater)));

        Tokenized(() => backend.ApplyComposed(typeof(Boxed<string>).GetMethod(nameof(Boxed<string>.Ping))!, [Marker("generic")]));

        Assert.True(publisher.IsHandedOff(BodyScope.KeyOf(typeof(Boxed<int>).GetMethod(nameof(Boxed<int>.Ping))!)));
    }

    [Fact]
    public void ThePublishedBodyIsClaimedBeforeItIsWrittenAndTheClaimRecordsItsOwners() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Attributed))!;

        Publish(target, typeof(FakeUpdater));

        Assert.Equal([true], FakeUpdater.ClaimedAtWrite);
        WriterRecord record = registry.RecordFor(target)!;
        Assert.Equal(["test"], record.Owners);
        Assert.Equal(WriterRegistry.Fingerprint(FakeUpdater.Writes.Single().Body), record.Fingerprint);
        Assert.Equal(FakeUpdater.Writes.Single().Body.Length, record.BodyLength);
    }

    [Fact]
    public void AnotherConcordWritersTargetIsDetouredInsteadOfOverwritten() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Contended))!;
        processClaims[target] = "another.concord#deadbeef";

        RecordingBackend detours = Publish(target, typeof(FakeUpdater));

        Assert.Empty(FakeUpdater.Writes);
        Assert.Empty(FakeUpdater.Reverts);
        Assert.Single(detours.Composed);
        Assert.Equal("another.concord#deadbeef", processClaims[target]);
        Assert.False(registry.HoldsClaim(target));
        Assert.Contains(WriterRegistry.CollisionMarker, string.Join("\n", registryLog));
    }

    [Fact]
    public void AHarmonyOwnedTargetIsNeitherClaimedNorWritten() {
        FakeUpdater.Reset();
        List<string> logged = [];
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = logged.Add;
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Owned))!;
        ForeignPatcherPolicy.ResetForTests();
        ForeignPatcherPolicy.Owners = method => method == target ? ["harmony.somemod"] : null;

        try {
            RecordingBackend detours = Publish(target, typeof(FakeUpdater));

            Assert.Empty(FakeUpdater.Writes);
            Assert.Empty(FakeUpdater.Reverts);
            Assert.Single(detours.Composed);
            Assert.False(registry.HoldsClaim(target));
            Assert.Contains(ForeignPatcherPolicy.ContestedMarker, string.Join("\n", logged));
        } finally {
            WaylithDetourBackend.Log = previous;
            ForeignPatcherPolicy.ResetForTests();
        }
    }

    [Fact]
    public void ATargetHarmonyTakesAfterConcordPublishedItHasItsComposedBodyTakenBackOff() {
        FakeUpdater.Reset();
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Seized))!;
        ForeignPatcherPolicy.ResetForTests();

        try {
            RecordingBackend detours = new RecordingBackend();
            ComposedBodyPublisher publisher = Publisher(detours, typeof(FakeUpdater), target);

            Assert.Single(FakeUpdater.Writes);
            Assert.True(registry.HoldsClaim(target));

            ForeignPatcherPolicy.Owners = method => method == target ? ["harmony.somemod"] : null;
            Tokenized(() => {
                publisher.PublishComposed(target, [Marker("test"), Marker("other")]);
                return new RecordingHandle();
            });

            Assert.Equal(target, FakeUpdater.Reverts.Single());
            Assert.Single(FakeUpdater.Writes);
            Assert.Single(detours.Composed);
            Assert.False(registry.HoldsClaim(target));
        } finally {
            WaylithDetourBackend.Log = previous;
            ForeignPatcherPolicy.ResetForTests();
        }
    }

    [Fact]
    public void RevertingAReplacedTargetReleasesItsClaimAndForgetsItsAttribution() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Dropped))!;
        ComposedBodyPublisher publisher = Publisher(new RecordingBackend(), typeof(FakeUpdater), target);

        Assert.True(registry.HoldsClaim(target));
        Assert.Equal(["test"], FrameAttribution.OwnersRegisteredFor(target));

        publisher.RevertToOriginal(target);

        Assert.Equal(target, FakeUpdater.Reverts.Single());
        Assert.False(registry.HoldsClaim(target));
        Assert.Empty(FrameAttribution.OwnersRegisteredFor(target));
    }

    [Fact]
    public void AWriteTheRuntimeRefusesReleasesTheClaimItTookForIt() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Refuted))!;
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };

        try {
            Publish(target, typeof(ThrowingUpdater));

            Assert.False(registry.HoldsClaim(target));
            Assert.Empty(FrameAttribution.OwnersRegisteredFor(target));
        } finally {
            WaylithDetourBackend.Log = previous;
        }
    }

    [Fact]
    public void DisposingAForwarderReleasesTheClaimItTookForIt() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Surrendered))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));

        IDetourHandle handle = Tokenized(
            () => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));

        Assert.Equal([true], FakeUpdater.ClaimedAtWrite);
        Assert.True(registry.HoldsClaim(target));

        handle.Dispose();

        Assert.False(registry.HoldsClaim(target));
    }

    [Fact]
    public void AForwarderOverAnotherConcordWritersTargetFallsBackToADetour() {
        FakeUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Forwarded))!;
        processClaims[target] = "another.concord#deadbeef";

        IDetourHandle handle = Tokenized(
            () => Backend(detours, typeof(FakeUpdater)).Apply(
                target,
                typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));

        Assert.Same(detours.Handles.Single(), handle);
        Assert.Empty(FakeUpdater.Writes);
        Assert.Equal("another.concord#deadbeef", processClaims[target]);
    }

    [Fact]
    public void AClosedGenericTargetIsComposedThroughTheDetourBackendBecauseItsBodyIsShared() {
        FakeUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        MethodInfo target = typeof(Boxed<string>).GetMethod(nameof(Boxed<string>.Ping))!;

        IDetourHandle handle = Tokenized(
            () => Backend(detours, typeof(FakeUpdater)).ApplyComposed(target, [Marker("generic")]));

        Assert.Same(detours.Handles.Single(), handle);
        Assert.Empty(FakeUpdater.Writes);
    }

    [Fact]
    public void AnOpenGenericDefinitionIsRefusedRatherThanComposedOverEveryInstantiation() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(Boxed<>).GetMethod(nameof(Boxed<string>.Ping))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(FakeUpdater));

        ConcordEmitException failure = Assert.Throws<ConcordEmitException>(
            () => backend.ApplyComposed(target, [Marker("open")]));

        Assert.Equal(SharedGenericPolicy.OpenDefinitionCode, failure.Code);
        Assert.Empty(FakeUpdater.Writes);
    }

    [Fact]
    public void AComposedBodyIsAttributedToTheOwnerOfTheInjectionThatEmittedIt() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Claimed))!;

        Publish(target, typeof(FakeUpdater));

        Assert.Equal(["test"], FrameAttribution.OwnersRegisteredFor(target));
        Assert.Equal("test", FrameAttribution.OwnerAt(target, Len(target)));
        Assert.Null(FrameAttribution.OwnerAt(target, 0));
    }

    [Fact]
    public void AHandOffBeforeAnythingWasWrittenNeitherRevertsTheTargetNorReportsAFailedUndo() {
        UntouchedRevertUpdater.Reverts.Clear();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Untouched))!;
        RecordingBackend detours = new RecordingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(
            detours,
            new ForkBodyWriter(typeof(UntouchedRevertUpdater)));
        List<string> logged = [];
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        Action<string> log = WaylithDetourBackend.Log;
        WaylithDetourBackend.Tokens = static _ => throw new InvalidOperationException("no token source on this module");
        WaylithDetourBackend.Log = logged.Add;

        try {
            publisher.PublishComposed(target, [Marker("test")]);
        } finally {
            WaylithDetourBackend.Tokens = tokens;
            WaylithDetourBackend.Log = log;
        }

        Assert.Empty(UntouchedRevertUpdater.Reverts);
        Assert.False(registry.HoldsClaim(target));
        Assert.Single(detours.Composed);
        Assert.DoesNotContain(
            logged,
            line => line.StartsWith(WaylithTargetStack.UndoFailedMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void AFailedUndoWhileWithdrawingFromAContestedTargetSaysTheComposedBodyStayedOn() {
        UntouchedRevertUpdater.Reverts.Clear();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Withheld))!;
        List<string> logged = [];

        RecordingBackend detours = Contest(target, typeof(UntouchedRevertUpdater), logged);

        Assert.Equal([target], UntouchedRevertUpdater.Reverts);
        Assert.Single(detours.Composed);
        Assert.Contains(logged, line => line.StartsWith(ForeignPatcherPolicy.ContestedMarker, StringComparison.Ordinal));
        Assert.Equal(
            WaylithTargetStack.UndoFailedMarker + " '" + typeof(PatchSubject).FullName + "." + target.Name +
            "': InvalidOperationException, that method never carried a replaced body. Its composed body could not " +
            "be taken back off, so the detour installed next runs over a body that is no longer the target's " +
            "original one.",
            Assert.Single(logged, line => line.StartsWith(WaylithTargetStack.UndoFailedMarker, StringComparison.Ordinal)));
    }

    [Fact]
    public void AWithdrawalFromAContestedTargetThatRevertsCleanlyReportsNoFailedUndo() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Handed))!;
        List<string> logged = [];

        RecordingBackend detours = Contest(target, typeof(FakeUpdater), logged);

        Assert.Equal([target], FakeUpdater.Reverts);
        Assert.Single(detours.Composed);
        Assert.Contains(logged, line => line.StartsWith(ForeignPatcherPolicy.ContestedMarker, StringComparison.Ordinal));
        Assert.DoesNotContain(
            logged,
            line => line.StartsWith(WaylithTargetStack.UndoFailedMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void APublishAsksTheForeignPatcherWhoOwnsTheTargetExactlyOnce() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Questioned))!;
        ForeignPatcherPolicy.ResetForTests();
        int probes = 0;
        ForeignPatcherPolicy.Owners = _ => {
            probes++;
            return null;
        };

        try {
            Publish(target, typeof(FakeUpdater));

            Assert.Single(FakeUpdater.Writes);
            Assert.Equal(1, probes);
        } finally {
            ForeignPatcherPolicy.ResetForTests();
        }
    }

    [Fact]
    public void TwoThreadsForwardingOneTargetWriteOneBodyAndTheSecondIsRefused() {
        ConcurrentUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Raced))!;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(ConcurrentUpdater));
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        ConcurrentQueue<IDetourHandle> applied = new ConcurrentQueue<IDetourHandle>();
        ConcurrentQueue<Exception> refused = new ConcurrentQueue<Exception>();
        ManualResetEventSlim released = new ManualResetEventSlim(false);
        int arrived = 0;

        void Arrive() {
            if (Interlocked.Increment(ref arrived) >= 2) {
                released.Set();
            }
        }

        WaylithDetourBackend.Tokens = _ => {
            Arrive();
            released.Wait(TimeSpan.FromSeconds(5));
            return new Tokens();
        };

        try {
            Thread[] racers = new Thread[2];
            for (int i = 0; i < racers.Length; i++) {
                racers[i] = new Thread(() => {
                    try {
                        applied.Enqueue(backend.Apply(target, replacement));
                    } catch (Exception failure) {
                        refused.Enqueue(failure);
                        Arrive();
                    }
                });
                racers[i].Start();
            }

            foreach (Thread racer in racers) {
                racer.Join();
            }
        } finally {
            WaylithDetourBackend.Tokens = tokens;
            released.Dispose();
        }

        Assert.Single(ConcurrentUpdater.Writes);
        Assert.Single(applied);
        Assert.Contains(
            "already carries a forwarder body",
            Assert.IsType<InvalidOperationException>(Assert.Single(refused)).Message);

        applied.Single().Dispose();
    }

    [Fact]
    public void ForwardingAndComposingOneTargetAtOnceWritesOneBodyAndRefusesTheLoserEveryRace() {
        const int Rounds = RaceSubjects.Count;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        ForkBodyWriter writer = new ForkBodyWriter(typeof(ConcurrentUpdater));
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        Action<string> log = WaylithDetourBackend.Log;
        Barrier aligned = new Barrier(3);
        List<string> broken = [];

        WaylithDetourBackend.Tokens = static _ => new Tokens();
        WaylithDetourBackend.Log = static _ => { };

        try {
            for (int round = 0; round < Rounds; round++) {
                MethodInfo target = RaceSubjects.At(round);
                ConcurrentUpdater.Reset();
                RecordingBackend detours = new RecordingBackend();
                WaylithDetourBackend backend = new WaylithDetourBackend(
                    detours, new ComposedBodyPublisher(detours, writer), writer);
                ConcurrentQueue<IDetourHandle> applied = new ConcurrentQueue<IDetourHandle>();
                ConcurrentQueue<Exception> refused = new ConcurrentQueue<Exception>();
                PublishSeam rival = new PublishSeam([Marker("rival")], SeamHoldMilliseconds);
                int release = 0;

                void Race(Func<IDetourHandle> work) {
                    aligned.SignalAndWait();
                    while (Volatile.Read(ref release) == 0) {
                    }

                    try {
                        applied.Enqueue(work());
                    } catch (Exception failure) {
                        refused.Enqueue(failure);
                    }
                }

                Thread[] racers = [
                    new Thread(() => Race(() => {
                        rival.Reached.Wait(TimeSpan.FromSeconds(2));
                        return backend.Apply(target, replacement);
                    })) { IsBackground = true },
                    new Thread(() => Race(() => {
                        try {
                            return backend.ApplyComposed(target, rival);
                        } finally {
                            rival.Reached.Set();
                        }
                    })) { IsBackground = true },
                ];

                foreach (Thread racer in racers) {
                    racer.Start();
                }

                aligned.SignalAndWait();
                Volatile.Write(ref release, 1);
                foreach (Thread racer in racers) {
                    racer.Join();
                }

                bool settled = rival.Fired &&
                    ConcurrentUpdater.Writes.Count == 1 &&
                    applied.Count == 1 &&
                    applied.Single().IsApplied &&
                    detours.Handles.Count == 0 &&
                    refused.Count == 1 &&
                    refused.Single() is InvalidOperationException &&
                    refused.Single().Message.Contains("already carries a composed body", StringComparison.Ordinal) &&
                    refused.Single().Message.Contains("a method has exactly one body", StringComparison.Ordinal);

                if (!settled) {
                    broken.Add(
                        $"round {round} '{target.Name}': seam={rival.Fired}, " +
                        $"writes={ConcurrentUpdater.Writes.Count}, " +
                        $"sizes=[{string.Join(", ", ConcurrentUpdater.Sizes)}], " +
                        $"applied=[{string.Join(", ", applied.Select(handle => handle.IsApplied))}], " +
                        $"detours={detours.Handles.Count}, " +
                        $"refused=[{string.Join(", ", refused.Select(failure => failure.GetType().Name))}]");
                }

                foreach (IDetourHandle handle in applied) {
                    handle.Dispose();
                }

                rival.Dispose();
            }
        } finally {
            WaylithDetourBackend.Tokens = tokens;
            WaylithDetourBackend.Log = log;
            aligned.Dispose();
        }

        Assert.True(
            broken.Count == 0,
            $"{broken.Count} of {Rounds} races did not resolve to one body and one refusal: " +
            string.Join(" | ", broken.Take(5)));
    }

    [Fact]
    public void DisposingAForwarderWhileAComposedBodyIsPublishedOnItsKeyStillRevertsThatBodyEveryRace() {
        const int Rounds = RevertRaceSubjects.Count;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        ForkBodyWriter writer = new ForkBodyWriter(typeof(ConcurrentUpdater));
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        Action<string> log = WaylithDetourBackend.Log;
        Barrier aligned = new Barrier(3);
        List<string> broken = [];

        WaylithDetourBackend.Tokens = static _ => new Tokens();
        WaylithDetourBackend.Log = static _ => { };

        try {
            for (int round = 0; round < Rounds; round++) {
                MethodInfo target = RevertRaceSubjects.At(round);
                ConcurrentUpdater.Reset();
                ReleaseSeam seam = new ReleaseSeam(SeamHoldMilliseconds);
                WriterRegistry rounds = new WriterRegistry(
                    static _ => { },
                    new ConcurrentDictionary<MethodBase, string>(seam));
                WaylithDetourBackend.Writers = rounds;
                RecordingBackend detours = new RecordingBackend();
                WaylithDetourBackend backend = new WaylithDetourBackend(
                    detours, new ComposedBodyPublisher(detours, writer), writer);
                IDetourHandle forwarder = backend.Apply(target, replacement);
                bool forwarderLive = forwarder.IsApplied && ConcurrentUpdater.Writes.Count == 1;
                ConcurrentQueue<IDetourHandle> composed = new ConcurrentQueue<IDetourHandle>();
                ConcurrentQueue<Exception> refused = new ConcurrentQueue<Exception>();
                int release = 0;

                void Race(Action work) {
                    aligned.SignalAndWait();
                    while (Volatile.Read(ref release) == 0) {
                    }

                    try {
                        work();
                    } catch (Exception failure) {
                        refused.Enqueue(failure);
                    }
                }

                Thread remover = new Thread(() => Race(forwarder.Dispose)) { IsBackground = true };
                seam.Held = remover;
                Thread publisher = new Thread(() => Race(() => {
                    seam.Reached.Wait(TimeSpan.FromSeconds(2));
                    composed.Enqueue(backend.ApplyComposed(target, [Marker("rival")]));
                })) { IsBackground = true };

                remover.Start();
                publisher.Start();
                aligned.SignalAndWait();
                Volatile.Write(ref release, 1);
                remover.Join();
                publisher.Join();

                bool entered = forwarderLive && seam.Fired && refused.IsEmpty && composed.Count == 1 &&
                    ConcurrentUpdater.Writes.Count == 2;
                bool claimed = rounds.HoldsClaim(target);
                if (entered) {
                    composed.Single().Dispose();
                }

                if (!entered || ConcurrentUpdater.Reverts.Count != 2) {
                    broken.Add(
                        $"round {round} '{target.Name}': forwarder={forwarderLive}, seam={seam.Fired}, " +
                        $"writes={ConcurrentUpdater.Writes.Count}, composed={composed.Count}, " +
                        $"claimedAfterRace={claimed}, reverts={ConcurrentUpdater.Reverts.Count}, " +
                        $"refused=[{string.Join(", ", refused.Select(failure => failure.GetType().Name))}]");
                }

                seam.Dispose();
            }
        } finally {
            WaylithDetourBackend.Tokens = tokens;
            WaylithDetourBackend.Log = log;
            WaylithDetourBackend.Writers = registry;
            aligned.Dispose();
        }

        Assert.True(
            broken.Count == 0,
            $"{broken.Count} of {Rounds} races left a composed body installed that nothing reverts: " +
            string.Join(" | ", broken.Take(5)));
    }

    [Fact]
    public void DisposingAComposedBodyWhileASecondBackendForwardsItsKeyKeepsTheForwardersClaimEveryRace() {
        const int Rounds = ClaimRaceSubjects.Count;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        ForkBodyWriter writer = new ForkBodyWriter(typeof(HoldingUpdater));
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        Action<string> log = WaylithDetourBackend.Log;
        Barrier aligned = new Barrier(3);
        List<string> broken = [];
        int overlapped = 0;

        WaylithDetourBackend.Tokens = static _ => new Tokens();
        WaylithDetourBackend.Log = static _ => { };
        HoldingUpdater.Hold = SeamHoldMilliseconds;

        try {
            for (int round = 0; round < Rounds; round++) {
                MethodInfo target = ClaimRaceSubjects.At(round);
                HoldingUpdater.Reset();
                ForeignPatcherPolicy.ResetForTests();
                WriterRegistry rounds = new WriterRegistry(
                    static _ => { }, new ConcurrentDictionary<MethodBase, string>());
                WaylithDetourBackend.Writers = rounds;
                RecordingBackend detours = new RecordingBackend();
                WaylithDetourBackend backend = new WaylithDetourBackend(
                    detours, new ComposedBodyPublisher(detours, writer), writer);
                WaylithDetourBackend rival = new WaylithDetourBackend(
                    detours, new ComposedBodyPublisher(detours, writer), writer);
                IDetourHandle composed = backend.ApplyComposed(target, [Marker("owner")]);
                bool composedLive = composed.IsApplied && rounds.HoldsClaim(target) &&
                    HoldingUpdater.Writes.Count == 1;
                ConcurrentQueue<IDetourHandle> applied = new ConcurrentQueue<IDetourHandle>();
                ConcurrentQueue<Exception> refused = new ConcurrentQueue<Exception>();
                ConcurrentQueue<string> finished = new ConcurrentQueue<string>();
                bool inside = false;
                int release = 0;

                void Race(Action work) {
                    aligned.SignalAndWait();
                    while (Volatile.Read(ref release) == 0) {
                    }

                    try {
                        work();
                    } catch (Exception failure) {
                        refused.Enqueue(failure);
                    }
                }

                Thread remover = new Thread(() => Race(() => {
                    composed.Dispose();
                    finished.Enqueue("removed");
                })) { IsBackground = true };
                HoldingUpdater.Held = remover;
                Thread forwarder = new Thread(() => Race(() => {
                    HoldingUpdater.Reached.Wait(TimeSpan.FromSeconds(2));
                    IDetourHandle handle = rival.Apply(target, replacement);
                    inside = HoldingUpdater.Holding;
                    finished.Enqueue("forwarded");
                    applied.Enqueue(handle);
                })) { IsBackground = true };

                remover.Start();
                forwarder.Start();
                aligned.SignalAndWait();
                Volatile.Write(ref release, 1);
                remover.Join();
                forwarder.Join();

                if (inside) {
                    overlapped++;
                }

                bool forwarderLive = applied.Count == 1 && applied.Single().IsApplied &&
                    HoldingUpdater.Writes.Count == 2;
                bool claimKept = rounds.HoldsClaim(target);
                ForeignPatcherPolicy.Owners = static _ => ["rival"];
                int reported = ForeignPatcherPolicy.Contentions().Count;

                if (!composedLive || !HoldingUpdater.Fired || !refused.IsEmpty || !forwarderLive || !claimKept ||
                    reported != 1 || inside) {
                    broken.Add(
                        $"round {round} '{target.Name}': composed={composedLive}, seam={HoldingUpdater.Fired}, " +
                        $"writes={HoldingUpdater.Writes.Count}, reverts={HoldingUpdater.Reverts.Count}, " +
                        $"forwarder=[{string.Join(", ", applied.Select(handle => handle.IsApplied))}], " +
                        $"claimKept={claimKept}, claims={rounds.ClaimCount}, reported={reported}, " +
                        $"inside={inside}, order=[{string.Join(", ", finished)}], " +
                        $"refused=[{string.Join(", ", refused.Select(failure => failure.GetType().Name))}]");
                }

                foreach (IDetourHandle handle in applied) {
                    handle.Dispose();
                }
            }
        } finally {
            WaylithDetourBackend.Tokens = tokens;
            WaylithDetourBackend.Log = log;
            WaylithDetourBackend.Writers = registry;
            ForeignPatcherPolicy.ResetForTests();
            HoldingUpdater.Reset();
            aligned.Dispose();
        }

        Assert.True(
            broken.Count == 0,
            $"{broken.Count} of {Rounds} rounds dropped the claim a concurrent forwarder had just taken, and " +
            $"{overlapped} of them let that forwarder publish while the composed dispose was still reverting: " +
            string.Join(" | ", broken.Take(5)));
    }

    [Fact]
    public void ATargetRefusedForHavingNoBodyCanBeForwardedAgainAfterwards() {
        ConcurrentUpdater.Reset();
        MethodInfo target = typeof(Shaped).GetMethod(nameof(Shaped.Missing))!;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(ShapedMissing))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(ConcurrentUpdater));

        ConcordEmitException first = Assert.Throws<ConcordEmitException>(
            () => Tokenized(() => backend.Apply(target, replacement)));
        ConcordEmitException second = Assert.Throws<ConcordEmitException>(
            () => Tokenized(() => backend.Apply(target, replacement)));

        Assert.Equal(TargetEligibility.NoBodyCode, first.Code);
        Assert.Equal(TargetEligibility.NoBodyCode, second.Code);
        Assert.Empty(ConcurrentUpdater.Writes);
    }

    [Fact]
    public void ATargetRefusedForAMismatchedReplacementCanBeForwardedAgainWithAMatchingOne() {
        ConcurrentUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Mismatched))!;
        WaylithDetourBackend backend = Backend(new RecordingBackend(), typeof(ConcurrentUpdater));

        Assert.Throws<NotSupportedException>(() => Tokenized(() => backend.Apply(target, TwoArgsWrong)));
        IDetourHandle handle = Tokenized(() => backend.Apply(target, TwoArgs));

        Assert.True(handle.IsApplied);
        Assert.Single(ConcurrentUpdater.Writes);

        handle.Dispose();
    }

    [Fact]
    public void ATargetWhoseModuleIsDynamicFallsBackEveryTimeItIsForwarded() {
        ConcurrentUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        DynamicMethod target = new DynamicMethod("raced", typeof(int), [typeof(int)]);
        ILGenerator il = target.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        WaylithDetourBackend backend = Backend(detours, typeof(ConcurrentUpdater));
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };

        try {
            Tokenized(() => backend.Apply(target, replacement));
            Tokenized(() => backend.Apply(target, replacement));
        } finally {
            WaylithDetourBackend.Log = previous;
        }

        Assert.Equal(2, detours.Handles.Count);
        Assert.Empty(ConcurrentUpdater.Writes);
    }

    [Fact]
    public void AnUnforwardableShapeFallsBackEveryTimeItIsForwarded() {
        ConcurrentUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Twinned))!;
        DynamicMethod replacement = new DynamicMethod("twinned", typeof(int), [typeof(int)]);
        ILGenerator il = replacement.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        WaylithDetourBackend backend = Backend(detours, typeof(ConcurrentUpdater));
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };

        try {
            Tokenized(() => backend.Apply(target, replacement));
            Tokenized(() => backend.Apply(target, replacement));
        } finally {
            WaylithDetourBackend.Log = previous;
        }

        Assert.Equal(2, detours.Handles.Count);
        Assert.Empty(ConcurrentUpdater.Writes);
        Assert.False(registry.HoldsClaim(target));
    }

    [Fact]
    public void ATargetAnotherConcordWriterHoldsFallsBackEveryTimeItIsForwarded() {
        ConcurrentUpdater.Reset();
        RecordingBackend detours = new RecordingBackend();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Preempted))!;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        processClaims[target] = "another.concord#deadbeef";
        WaylithDetourBackend backend = Backend(detours, typeof(ConcurrentUpdater));
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };

        try {
            Tokenized(() => backend.Apply(target, replacement));
            Tokenized(() => backend.Apply(target, replacement));
        } finally {
            WaylithDetourBackend.Log = previous;
        }

        Assert.Equal(2, detours.Handles.Count);
        Assert.Empty(ConcurrentUpdater.Writes);
        Assert.False(registry.HoldsClaim(target));
    }

    [Fact]
    public void ATargetTheRuntimeRefusesFallsBackEveryTimeItIsForwarded() {
        RecordingBackend detours = new RecordingBackend();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Balked))!;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        WaylithDetourBackend backend = Backend(detours, typeof(ThrowingUpdater));
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };

        try {
            Tokenized(() => backend.Apply(target, replacement));
            Tokenized(() => backend.Apply(target, replacement));
        } finally {
            WaylithDetourBackend.Log = previous;
        }

        Assert.Equal(2, detours.Handles.Count);
        Assert.False(registry.HoldsClaim(target));
    }

    [Fact]
    public void ASecondForwarderOverAForwarderIsRefusedEvenWhenItsReplacementWouldFallBack() {
        FakeUpdater.Reset();
        MethodInfo target = typeof(PatchSubject).GetMethod(nameof(PatchSubject.Overlaid))!;
        RecordingBackend detours = new RecordingBackend();
        WaylithDetourBackend backend = Backend(detours, typeof(FakeUpdater));
        IDetourHandle first = Tokenized(
            () => backend.Apply(target, typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!));
        DynamicMethod replacement = new DynamicMethod("overlaid", typeof(int), [typeof(int)]);
        ILGenerator il = replacement.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        Exception? second = Record.Exception(() => Tokenized(() => backend.Apply(target, replacement)));

        Assert.True(first.IsApplied);
        Assert.Empty(detours.Handles);
        Assert.Single(FakeUpdater.Writes);
        InvalidOperationException refusal = Assert.IsType<InvalidOperationException>(second);
        Assert.Contains("already carries a forwarder body", refusal.Message);

        first.Dispose();
    }

    [Fact]
    public void AGenericForwarderNeverHoldsTheSharedKeyAgainstASiblingInstantiation() {
        ConcurrentUpdater.Reset();
        MethodInfo generic = typeof(Crate<int>).GetMethod(nameof(Crate<object>.Get), [typeof(int)])!;
        MethodInfo sibling = typeof(Crate<string>).GetMethod(nameof(Crate<object>.Get), [typeof(int)])!;
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(CrateGet))!;
        GateBackend gate = new GateBackend();
        WaylithDetourBackend backend = new WaylithDetourBackend(
            gate,
            new ComposedBodyPublisher(gate, new ForkBodyWriter(typeof(ConcurrentUpdater))),
            new ForkBodyWriter(typeof(ConcurrentUpdater)));
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };
        Exception? forwarding = null;
        Exception? composing = null;
        IDetourHandle? composed = null;

        try {
            Thread racer = new Thread(() => {
                try {
                    backend.Apply(generic, replacement);
                } catch (Exception failure) {
                    forwarding = failure;
                    gate.Entered.Set();
                }
            });
            racer.Start();
            Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(30)));

            try {
                composed = backend.ApplyComposed(sibling, [Marker("sibling")]);
            } catch (Exception failure) {
                composing = failure;
            } finally {
                gate.Released.Set();
                racer.Join();
            }
        } finally {
            WaylithDetourBackend.Log = previous;
        }

        Assert.True(forwarding is null, forwarding?.Message);
        Assert.True(composing is null, composing?.Message);
        Assert.NotNull(composed);
        Assert.Single(gate.Composed);
        Assert.Empty(ConcurrentUpdater.Writes);
    }

    [Fact]
    public void ATargetThatFallsBackNeverHoldsItsOwnKeyAgainstASecondAttempt() {
        ConcurrentUpdater.Reset();
        DynamicMethod target = new DynamicMethod("gated", typeof(int), [typeof(int)]);
        ILGenerator il = target.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        MethodInfo replacement = typeof(WaylithDetourBackendTests).GetMethod(nameof(SumOne))!;
        GateBackend gate = new GateBackend();
        WaylithDetourBackend backend = new WaylithDetourBackend(
            gate,
            new ComposedBodyPublisher(gate, new ForkBodyWriter(typeof(ConcurrentUpdater))),
            new ForkBodyWriter(typeof(ConcurrentUpdater)));
        Action<string> previous = WaylithDetourBackend.Log;
        WaylithDetourBackend.Log = static _ => { };
        Exception? first = null;
        Exception? second = null;
        IDetourHandle? detoured = null;

        try {
            Thread racer = new Thread(() => {
                try {
                    backend.Apply(target, replacement);
                } catch (Exception failure) {
                    first = failure;
                    gate.Entered.Set();
                }
            });
            racer.Start();
            Assert.True(gate.Entered.Wait(TimeSpan.FromSeconds(30)));

            try {
                detoured = backend.Apply(target, replacement);
            } catch (Exception failure) {
                second = failure;
            } finally {
                gate.Released.Set();
                racer.Join();
            }
        } finally {
            WaylithDetourBackend.Log = previous;
        }

        Assert.True(first is null, first?.Message);
        Assert.True(second is null, second?.Message);
        Assert.NotNull(detoured);
        Assert.Empty(ConcurrentUpdater.Writes);
    }

    private RecordingBackend Contest(MethodInfo target, Type updater, List<string> logged) {
        RecordingBackend detours = new RecordingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(detours, new ForkBodyWriter(updater));
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        Action<string> log = WaylithDetourBackend.Log;
        bool owned = false;
        ForeignPatcherPolicy.ResetForTests();
        ForeignPatcherPolicy.Owners = _ => owned ? new string[] { "rival.mod" } : null;
        WaylithDetourBackend.Tokens = static _ => new Tokens();
        WaylithDetourBackend.Log = logged.Add;

        try {
            _ = new WaylithDetourBackend(detours, publisher, new ForkBodyWriter(updater));
            publisher.PublishComposed(target, [Marker("test")]);
            Assert.True(registry.HoldsClaim(target));
            owned = true;
            publisher.PublishComposed(target, [Marker("test")]);
        } finally {
            WaylithDetourBackend.Tokens = tokens;
            WaylithDetourBackend.Log = log;
            ForeignPatcherPolicy.ResetForTests();
        }

        return detours;
    }

    private static WaylithDetourBackend Backend(RecordingBackend detours, Type updater) {
        return new WaylithDetourBackend(
            detours,
            new ComposedBodyPublisher(detours, new ForkBodyWriter(updater)),
            new ForkBodyWriter(updater));
    }

    private static IDetourHandle Tokenized(Func<IDetourHandle> work) {
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        WaylithDetourBackend.Tokens = static _ => new Tokens();
        try {
            return work();
        } finally {
            WaylithDetourBackend.Tokens = tokens;
        }
    }

    private static RecordingBackend Publish(MethodInfo target, Type updater) {
        RecordingBackend detours = new RecordingBackend();
        Publisher(detours, updater, target);
        return detours;
    }

    private static ComposedBodyPublisher Publisher(RecordingBackend detours, Type updater, MethodInfo target) {
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(detours, new ForkBodyWriter(updater));
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        WaylithDetourBackend.Tokens = static _ => new Tokens();
        try {
            _ = new WaylithDetourBackend(detours, publisher, new ForkBodyWriter(updater));
            publisher.PublishComposed(target, [Marker("test")]);
        } finally {
            WaylithDetourBackend.Tokens = tokens;
        }

        return publisher;
    }

    private static Injection LocalHead(string owner) {
        return new Injection(
            typeof(PatchSubject).GetMethod(nameof(PatchSubject.LocalAtHead))!, new InjectAt.Head(), owner, 0);
    }

    private static Injection Dummy(string owner) {
        return new Injection(TwoArgs, new InjectAt.Head(), owner, 0);
    }

    private static Injection Marker(string owner) {
        return new Injection(
            typeof(PatchSubject).GetMethod(nameof(PatchSubject.Marker))!, new InjectAt.Head(), owner, 0);
    }

    private static Injection Second(string owner) {
        return new Injection(
            typeof(PatchSubject).GetMethod(nameof(PatchSubject.SecondMarker))!, new InjectAt.Head(), owner, 1);
    }

    private static void PublishOwned(MethodInfo target, List<string> logged, params Injection[] ordered) {
        RecordingBackend detours = new RecordingBackend();
        ComposedBodyPublisher publisher = new ComposedBodyPublisher(detours, new ForkBodyWriter(typeof(FakeUpdater)));
        Func<Module, ITokenSource> tokens = WaylithDetourBackend.Tokens;
        Action<string> log = WaylithDetourBackend.Log;
        WaylithDetourBackend.Tokens = static _ => new Tokens();
        WaylithDetourBackend.Log = logged.Add;

        try {
            _ = new WaylithDetourBackend(detours, publisher, new ForkBodyWriter(typeof(FakeUpdater)));
            publisher.PublishComposed(target, ordered);
        } finally {
            WaylithDetourBackend.Tokens = tokens;
            WaylithDetourBackend.Log = log;
        }
    }

    private static IReadOnlyList<OffsetPair> TwoOwnerMap() {
        ComposedBody body = new ComposedBody(
            [Copied(OpCodes.Nop, 5), Injected(OpCodes.Nop), Copied(OpCodes.Ret, 9)],
            [],
            [],
            false);
        EncodedBody encoded = new EncodedBody([], [0, 1, 2], 1, 3);

        return WaylithDetourBackend.OffsetMap(Spanned, body, encoded, [Marker("alpha"), Second("beta")]);
    }

    private static ComposedInstruction Copied(OpCode opcode, int origin) {
        return new ComposedInstruction(new CodeInstruction(opcode), origin, null);
    }

    private static ComposedInstruction Injected(OpCode opcode) {
        return new ComposedInstruction(new CodeInstruction(opcode), null, TwoArgs);
    }

    private static ComposedInstruction InjectedBy(OpCode opcode, string injection) {
        return new ComposedInstruction(
            new CodeInstruction(opcode), null, typeof(PatchSubject).GetMethod(injection)!);
    }

    private static ComposedInstruction Rewritten(OpCode opcode) {
        return new ComposedInstruction(new CodeInstruction(opcode), null, null);
    }

    private static int Len(MethodBase target) {
        return target.GetMethodBody()!.GetILAsByteArray()!.Length;
    }

    public sealed class Holder {
        public int Read(int value) {
            return value;
        }
    }

    public readonly struct Counted {
        public int Read(int value) {
            return value;
        }
    }

    public static class WrongShapeUpdater {
        public static void ReplaceMethodBody(MethodBase method, byte[] methodBody) {
        }

        public static void Revert(MethodBase method) {
        }
    }
}
