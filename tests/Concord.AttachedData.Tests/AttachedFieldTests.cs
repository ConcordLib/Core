using System.Threading.Tasks;
using Xunit;

namespace Concord.AttachedData.Tests;

public sealed class Holder { }

public sealed class AttachedFieldTests {
    [Fact]
    public void Get_AbsentTarget_ReturnsDefault() {
        AttachedField<Holder, int> field = new AttachedField<Holder, int>();
        Assert.Equal(0, field.Get(new Holder()));
    }

    [Fact]
    public void SetThenGet_RoundTrips() {
        AttachedField<Holder, int> field = new AttachedField<Holder, int>();
        Holder h = new Holder();
        field.Set(h, 42);
        Assert.Equal(42, field.Get(h));
    }

    [Fact]
    public void Set_Twice_Overwrites() {
        AttachedField<Holder, int> field = new AttachedField<Holder, int>();
        Holder h = new Holder();
        field.Set(h, 1);
        field.Set(h, 2);
        Assert.Equal(2, field.Get(h));
    }

    [Fact]
    public void SeparateInstances_AreIndependent() {
        AttachedField<Holder, int> field = new AttachedField<Holder, int>();
        Holder a = new Holder();
        Holder b = new Holder();
        field.Set(a, 10);
        Assert.Equal(10, field.Get(a));
        Assert.Equal(0, field.Get(b));
    }

    [Fact]
    public void TryGet_Present_ReturnsTrueAndValue() {
        AttachedField<Holder, int> field = new AttachedField<Holder, int>();
        Holder h = new Holder();
        field.Set(h, 7);
        bool found = field.TryGet(h, out int value);
        Assert.True(found);
        Assert.Equal(7, value);
    }

    [Fact]
    public void TryGet_Absent_ReturnsFalseAndDefault() {
        AttachedField<Holder, int> field = new AttachedField<Holder, int>();
        bool found = field.TryGet(new Holder(), out int value);
        Assert.False(found);
        Assert.Equal(0, value);
    }

    [Fact]
    public void Set_ConcurrentFirstWrite_DoesNotThrow() {
        AttachedField<object, int> field = new AttachedField<object, int>();
        object target = new object();

        Parallel.For(0, 64, _ => field.Set(target, 1));

        Assert.Equal(1, field.Get(target));
    }
}
