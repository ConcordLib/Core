using System.Linq;
using System.Reflection;
using Xunit;

namespace Concord.Emit.Tests;

public sealed class AtOrdinalTests {
    private static readonly (string Name, int Value)[] Declared = [
        ("Head", 0),
        ("Return", 1),
        ("Tail", 2),
        ("Around", 3),
        ("Constant", 4),
        ("Argument", 5),
        ("Transpiler", 6),
        ("TranspilerFinal", 7),
        ("Finally", 8),
        ("Local", 9),
        ("Catch", 10),
    ];

    [Fact]
    public void At_MembersKeepTheirDeclaredOrdinals() {
        (string Name, int Value)[] actual = typeof(At)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (field.Name, (int)field.GetRawConstantValue()!))
            .ToArray();

        Assert.Equal(Declared, actual);
    }
}
