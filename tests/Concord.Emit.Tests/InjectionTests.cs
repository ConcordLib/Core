using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class InjectionTests {
    [Fact]
    public void FourArgumentConstructor_LeavesOrderOwnersEmpty() {
        MethodInfo method = typeof(InjectionTests).GetMethod(nameof(Inject), BindingFlags.NonPublic | BindingFlags.Static)!;

        Injection injection = new Injection(method, new InjectAt.Head(), "test", 0);

        Assert.Empty(injection.BeforeOwners);
        Assert.Empty(injection.AfterOwners);
    }

    [Fact]
    public void OrderOwners_CanBeSetForImperativeInjection() {
        MethodInfo method = typeof(InjectionTests).GetMethod(nameof(Inject), BindingFlags.NonPublic | BindingFlags.Static)!;

        Injection injection = new Injection(method, new InjectAt.Head(), "test", 0) {
            BeforeOwners = ["before"],
            AfterOwners = ["after"]
        };

        Assert.Equal(["before"], injection.BeforeOwners);
        Assert.Equal(["after"], injection.AfterOwners);
    }

    private static void Inject() { }
}
