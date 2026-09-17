#nullable disable

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class HostTestTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Plain()
        {
        }

        public static void Injection()
        {
        }
    }

    public sealed class SupportMatrixHostTests
    {
        private static MethodBase Target => typeof(HostTestTargets).GetMethod(nameof(HostTestTargets.Plain));

        private static IReadOnlyList<Injection> HeadInjection()
        {
            MethodInfo method = typeof(HostTestTargets).GetMethod(nameof(HostTestTargets.Injection));
            return new List<Injection> { new Injection(method, new InjectAt.Head(), "test", 0) };
        }

        [Fact]
        public void HasILManipulatorsReturnsFalseForNull()
        {
            Assert.False(SupportMatrix.HasILManipulators(null));
        }

        [Fact]
        public void HasILManipulatorsFalseWhenListEmpty()
        {
            Patches patchInfo = new Patches(new Patch[0], new Patch[0], new Patch[0], new Patch[0], new Patch[0]);
            Assert.False(SupportMatrix.HasILManipulators(patchInfo));
        }

        [Fact]
        public void ValidateRejectsTargetWithILManipulator()
        {
            MethodInfo manipulator = typeof(HostTestTargets).GetMethod(nameof(HostTestTargets.Injection));
            Patch entry = new Patch(manipulator, 0, "other.mod", Priority.Normal, new string[0], new string[0], false);
            Patches patchInfo = new Patches(new Patch[0], new Patch[0], new Patch[0], new Patch[0], new[] { entry });

            string reason = SupportMatrix.Validate(Target, HeadInjection(), patchInfo);

            Assert.NotNull(reason);
            Assert.Contains("IL manipulators", reason);
        }

        [Fact]
        public void ValidateAcceptsTargetWithOnlyPrefixes()
        {
            MethodInfo prefix = typeof(HostTestTargets).GetMethod(nameof(HostTestTargets.Injection));
            Patch entry = new Patch(prefix, 0, "other.mod", Priority.Normal, new string[0], new string[0], false);
            Patches patchInfo = new Patches(new[] { entry }, new Patch[0], new Patch[0], new Patch[0], new Patch[0]);

            string reason = SupportMatrix.Validate(Target, HeadInjection(), patchInfo);

            Assert.Null(reason);
        }
    }
}
