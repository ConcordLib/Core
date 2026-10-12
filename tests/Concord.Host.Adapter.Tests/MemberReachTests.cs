using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

[assembly: InternalsVisibleTo("Concord.Host.Adapter")]

namespace Concord.Host.Adapter.Tests;

internal sealed class Shut {
    public int Slot;

    public static void Peek() {
    }
}

public class Wide {
    public int Slot;

    private readonly int slot = 1;

    public static void Pub() {
    }

    internal static void Asm() {
    }

    protected static void Fam() {
    }

    private static void Priv() {
    }

    public static void Generic<T>() {
    }

    internal int Read => slot;
}

public class MemberReachTests {
    private static readonly Module Elsewhere = typeof(object).Module;

    private static readonly Module Here = typeof(MemberReachTests).Module;

    private static readonly Module Granted = typeof(WaylithTokenSource).Module;

    private static MethodBase Method(string name) {
        return typeof(Wide).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
    }

    [Fact]
    public void APublicMemberOfAnotherAssemblyIsReachable() {
        Assert.Null(MemberReach.Explain(typeof(List<int>).GetMethod("Add")!, Here));
        Assert.Null(MemberReach.Explain(typeof(Wide), Elsewhere));
        Assert.Null(MemberReach.Explain(Method("Pub"), Elsewhere));
        Assert.Null(MemberReach.Explain(typeof(Wide).GetField("Slot")!, Elsewhere));
        Assert.Null(MemberReach.Explain(typeof(Wide).GetConstructors()[0], Elsewhere));
    }

    [Fact]
    public void AMemberOnAnInternalTypeIsRefusedWithTheTypeAndTheFixNamed() {
        string reason = MemberReach.Explain(typeof(Shut).GetMethod("Peek")!, Elsewhere)!;

        Assert.Contains("the method 'Concord.Host.Adapter.Tests.Shut.Peek'", reason);
        Assert.Contains("'Concord.Host.Adapter.Tests.Shut' is not visible outside", reason);
        Assert.Contains("TypeAccessException", reason);
        Assert.Contains("InternalsVisibleTo(\"System.Private.CoreLib\")", reason);
    }

    [Fact]
    public void AnInternalTypeAsAnOperandIsRefused() {
        Assert.Contains("the type 'Concord.Host.Adapter.Tests.Shut'", MemberReach.Explain(typeof(Shut), Elsewhere)!);
    }

    [Fact]
    public void AConstructedGenericOverAnInternalTypeNamesTheInternalArgument() {
        string reason = MemberReach.Explain(typeof(List<Shut>), Elsewhere)!;

        Assert.Contains("'Concord.Host.Adapter.Tests.Shut' is not visible outside", reason);
    }

    [Fact]
    public void AConstructedGenericIsJudgedByItsArgumentsNotByItsOwnModule() {
        Assert.Null(MemberReach.Explain(typeof(List<Shut>), Here));
        Assert.Null(MemberReach.Explain(typeof(List<Shut>), Granted));
        Assert.Null(MemberReach.Explain(typeof(Shut[]), Here));
        Assert.Null(MemberReach.Explain(typeof(Dictionary<string, Shut>), Here));
    }

    [Fact]
    public void AnArrayOrByrefOfAnInternalTypeNamesTheElement() {
        Assert.Contains("'Concord.Host.Adapter.Tests.Shut' is not visible outside", MemberReach.Explain(typeof(Shut[]), Elsewhere)!);
        Assert.Contains("'Concord.Host.Adapter.Tests.Shut' is not visible outside", MemberReach.Explain(typeof(Shut).MakeByRefType(), Elsewhere)!);
    }

    [Fact]
    public void AGenericMethodInstantiatedOverAnInternalTypeIsRefused() {
        MethodInfo empty = typeof(Enumerable)
            .GetMethods()
            .Single(candidate => candidate.Name == nameof(Enumerable.Empty) && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(Shut));

        string reason = MemberReach.Explain(empty, Elsewhere)!;

        Assert.Contains("'Concord.Host.Adapter.Tests.Shut' is not visible outside", reason);
    }

    [Fact]
    public void PrivateAndInternalMembersOfAVisibleTypeAreRefusedByAccessLevel() {
        Assert.Contains("declared private", MemberReach.Explain(Method("Priv"), Elsewhere)!);
        Assert.Contains("declared internal", MemberReach.Explain(Method("Asm"), Elsewhere)!);
        Assert.Contains("MethodAccessException", MemberReach.Explain(Method("Priv"), Elsewhere)!);
    }

    [Fact]
    public void APrivateFieldIsRefusedAndTheMessageNamesTheFieldException() {
        FieldInfo slot = typeof(Wide).GetField("slot", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string reason = MemberReach.Explain(slot, Elsewhere)!;

        Assert.Contains("the field 'Concord.Host.Adapter.Tests.Wide.slot'", reason);
        Assert.Contains("FieldAccessException", reason);
    }

    [Fact]
    public void AProtectedMemberIsNotRefusedBecauseADerivedTypeElsewhereCanReachIt() {
        Assert.Null(MemberReach.Explain(Method("Fam"), Elsewhere));
    }

    [Fact]
    public void AMemberInTheTargetsOwnAssemblyIsNeverRefused() {
        Assert.Null(MemberReach.Explain(Method("Priv"), Here));
        Assert.Null(MemberReach.Explain(typeof(Shut).GetMethod("Peek")!, Here));
    }

    [Fact]
    public void InternalsVisibleToClearsTheRefusalForThatConsumerOnly() {
        MethodBase shut = typeof(Shut).GetMethod("Peek")!;

        Assert.Null(MemberReach.Explain(shut, Granted));
        Assert.NotNull(MemberReach.Explain(shut, Elsewhere));
    }

    [Fact]
    public void AGenericParameterIsNotRefused() {
        Assert.Null(MemberReach.Explain(typeof(List<>).GetGenericArguments()[0], Elsewhere));
    }

    [Fact]
    public void NullArgumentsAreRejected() {
        Assert.Throws<ArgumentNullException>(() => MemberReach.Explain(null!, Here));
        Assert.Throws<ArgumentNullException>(() => MemberReach.Explain(typeof(Wide), null!));
    }
}
