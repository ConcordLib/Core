using System;
using System.Reflection;
using Concord.Emit;
using HarmonyLib;
using Xunit;

namespace Concord.Harmony.Tests
{
    public sealed class SupportMatrixInnerPatchesTests
    {
        [Fact]
        public void HasInnerPatchesReturnsFalseForNull()
        {
            bool result = SupportMatrix.HasInnerPatches(null);
            Assert.False(result);
        }

        [Fact]
        public void HasInnerPatchesFalseForNormalTarget()
        {
            MethodBase target = Method(typeof(SupportMatrixTestTargets), nameof(SupportMatrixTestTargets.SimpleTarget));
            Patches? patchInfo = PatchProcessor.GetPatchInfo(target);

            string? reason = SupportMatrix.Validate(target, Array.Empty<Injection>(), patchInfo);
            Assert.Null(reason);

            bool hasInner = SupportMatrix.HasInnerPatches(patchInfo);
            Assert.False(hasInner);
        }

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }
    }

    public sealed class SupportMatrixInnerPatchesPresentTests
    {
        [Fact]
        public void HasInnerPatchesReturnsTrueWhenInnerPrefixesNonEmpty()
        {
            MethodInfo innerPatchMethod = Method(typeof(SupportMatrixTestInjections), nameof(SupportMatrixTestInjections.SimplePrefix));
            Patch innerPatch = new Patch(innerPatchMethod, 0, "test.inner.owner", 0, null, null, false);
            Patches patchInfo = new Patches(null, null, null, null, new Patch[] { innerPatch }, null);

            bool hasInner = SupportMatrix.HasInnerPatches(patchInfo);
            Assert.True(hasInner);

            MethodBase target = Method(typeof(SupportMatrixTestTargets), nameof(SupportMatrixTestTargets.SimpleTarget));
            string? reason = SupportMatrix.Validate(target, Array.Empty<Injection>(), patchInfo);
            Assert.Null(reason);
        }

        [Fact]
        public void HasInnerPatchesReturnsTrueWhenInnerPostfixesNonEmpty()
        {
            MethodInfo innerPatchMethod = Method(typeof(SupportMatrixTestInjections), nameof(SupportMatrixTestInjections.SimplePrefix));
            Patch innerPatch = new Patch(innerPatchMethod, 0, "test.inner.owner", 0, null, null, false);
            Patches patchInfo = new Patches(null, null, null, null, null, new Patch[] { innerPatch });

            bool hasInner = SupportMatrix.HasInnerPatches(patchInfo);
            Assert.True(hasInner);
        }

        private static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name) ?? throw new InvalidOperationException(type.Name + "." + name + " not found");
        }
    }
}
