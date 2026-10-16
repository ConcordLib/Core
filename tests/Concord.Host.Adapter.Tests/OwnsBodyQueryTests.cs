using System.Reflection;
using Concord.Detour;
using Concord.Emit;
using Xunit;

namespace Concord.Host.Adapter.Tests;

public sealed class OwnsBodyQueryTests : IDisposable {
    private readonly WriterRegistry previousRegistry = WaylithDetourBackend.Writers;

    public OwnsBodyQueryTests() {
        WaylithDetourBackend.Writers = new WriterRegistry(static _ => { });
    }

    public static int Subject() {
        return 1;
    }

    public void Dispose() {
        WaylithDetourBackend.Writers = previousRegistry;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TheBackendAnswersTheOwnsBodyQuery() {
        Assert.IsAssignableFrom<IOwnsBodyQuery>(Backend(new UnawareFallback()));
    }

    [Fact]
    public void AnUnwrittenTargetIsNotOwned() {
        IOwnsBodyQuery backend = Backend(new UnawareFallback());

        Assert.False(backend.OwnsBody(Target()));
    }

    [Fact]
    public void AClaimedTargetIsOwned() {
        IOwnsBodyQuery backend = Backend(new UnawareFallback());
        WaylithDetourBackend.Writers.Claim(Target(), [0x2A], ["test"], []);

        Assert.True(backend.OwnsBody(Target()));
    }

    [Fact]
    public void ATargetTheFallbackOwnsIsOwned() {
        IOwnsBodyQuery backend = Backend(new OwningFallback());

        Assert.True(backend.OwnsBody(Target()));
    }

    [Fact]
    public void AFallbackThatCannotAnswerDoesNotThrow() {
        IOwnsBodyQuery backend = Backend(new UnawareFallback());

        Assert.False(backend.OwnsBody(Target()));
    }

    private static MethodBase Target() {
        return typeof(OwnsBodyQueryTests).GetMethod(nameof(Subject))!;
    }

    private static WaylithDetourBackend Backend(IDetourBackend fallback) {
        return new WaylithDetourBackend(fallback);
    }

    private class UnawareFallback : IDetourBackend {
        public IDetourHandle Apply(MethodBase original, MethodInfo replacement) {
            throw new NotSupportedException();
        }

        public IDetourHandle ApplyComposed(MethodBase target, IReadOnlyList<Injection> added) {
            throw new NotSupportedException();
        }
    }

    private sealed class OwningFallback : UnawareFallback, IOwnsBodyQuery {
        public bool OwnsBody(MethodBase target) {
            return true;
        }
    }
}
