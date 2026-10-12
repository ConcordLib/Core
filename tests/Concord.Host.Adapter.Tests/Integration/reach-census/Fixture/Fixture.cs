using System;

#if BLANK
namespace NotHarmonyLib {
#else
namespace HarmonyLib {
#endif
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class HarmonyPatch : Attribute {
        public HarmonyPatch() { }

        public HarmonyPatch(Type declaringType) {
            DeclaringType = declaringType;
        }

        public Type DeclaringType { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPrefix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyPostfix : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyTranspiler : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class HarmonyFinalizer : Attribute { }
}

#if BLANK
namespace Concord.Waylith.ReachCensus.Fixture {
    using NotHarmonyLib;
#else
namespace Concord.Waylith.ReachCensus.Fixture {
    using HarmonyLib;
#endif

    [HarmonyPatch]
    public static class PublicOnPublic {
        public static void Prefix() { }
    }

    [HarmonyPatch]
    public static class PrivateOnPublic {
        private static void Prefix() { }
    }

    [HarmonyPatch]
    public static class InternalOnPublic {
        internal static void Postfix() { }
    }

    [HarmonyPatch]
    public class ProtectedOnPublic {
        protected static void Prefix() { }
    }

    [HarmonyPatch]
    public class ProtectedInternalOnPublic {
        protected internal static void Postfix() { }
    }

    [HarmonyPatch]
    public class PrivateProtectedOnPublic {
        private protected static void Prefix() { }
    }

    [HarmonyPatch]
    internal static class PublicOnInternal {
        public static void Prefix() { }
    }

    [HarmonyPatch]
    internal static class PrivateOnInternal {
        private static void Prefix() { }
    }

    public static class PublicOuter {
        [HarmonyPatch]
        public static class PublicNested {
            public static void Prefix() { }
        }

        [HarmonyPatch]
        private static class PrivateNested {
            public static void Prefix() { }
        }
    }

    [HarmonyPatch]
    public static class AttributedRoles {
        [HarmonyPrefix]
        private static void NamedNothingLikeAPrefix() { }

        [HarmonyTranspiler]
        private static object Rewrite(object instructions) {
            return instructions;
        }

        [HarmonyFinalizer]
        public static Exception Cleanup(Exception __exception) {
            return __exception;
        }
    }

    [HarmonyPatch]
    public static class Infrastructure {
        public static void Prefix() { }

        private static bool Prepare() {
            return true;
        }

        private static void Cleanup() { }

        private static object TargetMethod() {
            return null;
        }
    }

    public static class NotAPatchClassAtAll {
        private static void Prefix() { }

        public static void Postfix() { }
    }
}
