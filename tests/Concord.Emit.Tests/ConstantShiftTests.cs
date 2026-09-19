using System;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public class ConstantShiftHost {
    // Keep() stops the C# compiler folding this to a single literal in Release, so both 42s
    // survive as separate ldc.i4.s the matcher can count.
    public int Sum() {
        return (Keep(42) * 100) + Keep(42);
    }

    private static int Keep(int value) {
        return value;
    }
}

public class ConstantShiftMethods {
    // The body carries a 42 of its own, which is what an At.Constant(42) injection ordinarily
    // looks like.
    public int PlusFortyTwo(int original) {
        return original + 42;
    }

    public int PlusOne(int original) {
        return original + 1;
    }
}

public sealed class ConstantShiftTests {
    // At.Constant must count By against the spine as it was before any injection spliced into it.
    // The By:1 body contains its own 42, so counting live puts the By:2 splice on that copied
    // literal instead of the target's second one: 42 + 43 = 85, and 85 * 100 + 42 is 8542.
    // Counting against the pre-splice snapshot gives 84 * 100 + 43 = 8443.
    [Fact]
    public void By_CountsAgainstThePreSpliceSpine() {
        MethodBase target = typeof(ConstantShiftHost).GetMethod(nameof(ConstantShiftHost.Sum))!;
        MethodBase plusFortyTwo = typeof(ConstantShiftMethods).GetMethod(nameof(ConstantShiftMethods.PlusFortyTwo))!;
        MethodBase plusOne = typeof(ConstantShiftMethods).GetMethod(nameof(ConstantShiftMethods.PlusOne))!;

        Injection second = new Injection(plusOne, new InjectAt.Constant(42, 2), "test", 0);
        Injection first = new Injection(plusFortyTwo, new InjectAt.Constant(42, 1), "test", 0);

        ComposeResult result = WrapperComposer.Compose(target, [second, first]);
        Func<ConstantShiftHost, int> run = result.Wrapper.CreateDelegate<Func<ConstantShiftHost, int>>();

        Assert.Equal(8443, run(new ConstantShiftHost()));
    }
}
