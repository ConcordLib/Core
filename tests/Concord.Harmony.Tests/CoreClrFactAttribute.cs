using Xunit;

namespace Concord.Harmony.Tests;

public sealed class CoreClrFactAttribute : FactAttribute
{
    public CoreClrFactAttribute(string monoReason)
    {
#if NET472
        Skip = monoReason;
#endif
    }
}
