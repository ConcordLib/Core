#nullable disable

using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MonoMod.Cil;
using Xunit;

namespace Concord.Harmony.Tests
{
    public static class ManipulatedTargets
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Value()
        {
            return 1;
        }

        public static void Manipulate(ILContext il)
        {
        }
    }

    [Collection("HarmonySerial")]
    public sealed class ILManipulatorOwnerTests
    {
        [Fact]
        public void ForeignOwnersIncludesILManipulatorOwner()
        {
            MethodBase target = typeof(ManipulatedTargets).GetMethod(nameof(ManipulatedTargets.Value));
            MethodInfo manipulator = typeof(ManipulatedTargets).GetMethod(nameof(ManipulatedTargets.Manipulate));
            HarmonyLib.Harmony harmony = new HarmonyLib.Harmony("owner.tests.ilmanip");
            List<string> log = new List<string>();

            try
            {
                harmony.Patch(target, ilmanipulator: new HarmonyMethod(manipulator));
                HarmonyBridge bridge = new HarmonyBridge(log.Add);

                IReadOnlyList<string> owners = bridge.ForeignOwners(target);

                Assert.Contains("owner.tests.ilmanip", owners);
            }
            finally
            {
                harmony.UnpatchSelf();
            }
        }
    }
}
