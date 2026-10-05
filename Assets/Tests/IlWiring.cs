using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Overpower.Tests
{
    /// <summary>
    /// "The game calls the tested rule" checks: reads the method bodies of a type (and the lambdas and state machines nested in it) and says whether any of
    /// them calls a method or reads a field. It does not run the game, so it can prove a component still asks the pure rule that is tested, and it fails the
    /// moment someone puts the inline copy of the branching back. Name the method that must do the calling: a call made somewhere else in the same class
    /// proves nothing about the place that matters.
    /// </summary>
    internal static class IlWiring
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>True when the method called <paramref name="methodName"/> (or a lambda, iterator or async state machine the compiler made out of it)
        /// has an IL call, callvirt, newobj or field load of the target. A call in any other method of the class does not count, so moving the
        /// hand-off out of the method that matters fails the test that names it.</summary>
        public static bool Uses(System.Type owner, string methodName, MemberInfo target)
        {
            byte[] token = System.BitConverter.GetBytes(target.MetadataToken);
            string generated = "<" + methodName + ">"; // the compiler names a lambda "<Apply>b__12_0" and a state machine type "<Apply>d__3"
            var types = new List<System.Type> { owner };
            types.AddRange(owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            foreach (System.Type type in types)
            {
                bool wholeTypeIsTheMethod = type != owner && type.Name.StartsWith(generated); // a state machine class made for exactly this method
                foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                {
                    if (!wholeTypeIsTheMethod && method.Name != methodName && !method.Name.StartsWith(generated)) continue;
                    if (Reads(method, token)) return true;
                }
            }
            return false;
        }

        /// <summary>True when any method of the type has an IL call, callvirt, newobj or field load of the target.</summary>
        public static bool Uses(System.Type owner, MemberInfo target)
        {
            byte[] token = System.BitConverter.GetBytes(target.MetadataToken);
            var types = new List<System.Type> { owner };
            types.AddRange(owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            foreach (System.Type type in types)
                foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                    if (Reads(method, token)) return true;
            return false;
        }

        /// <summary>True when the method called <paramref name="methodName"/> (or its lambdas and state machine) STORES into the field (an IL stfld): the
        /// "this method remembers the value" check, which a read (ldfld) cannot show.</summary>
        public static bool Stores(System.Type owner, string methodName, FieldInfo field)
        {
            byte[] token = System.BitConverter.GetBytes(field.MetadataToken);
            string generated = "<" + methodName + ">";
            var types = new List<System.Type> { owner };
            types.AddRange(owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            foreach (System.Type type in types)
            {
                bool wholeTypeIsTheMethod = type != owner && type.Name.StartsWith(generated);
                foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                {
                    if (!wholeTypeIsTheMethod && method.Name != methodName && !method.Name.StartsWith(generated)) continue;
                    if (OffsetsOf(method, token, 0x7D).Count > 0) return true; // stfld
                }
            }
            return false;
        }

        /// <summary>The IL offsets of every call, callvirt or newobj of the target in one method body (empty when it is not called there). A method of
        /// ANOTHER assembly (Photon) is referenced by a token of this assembly's own, so every call's token is resolved and compared as a method, not as bytes.</summary>
        public static List<int> CallOffsets(MethodBase method, MethodBase target)
        {
            var found = new List<int>();
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return found;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F && il[i] != 0x73) continue;
                int token = System.BitConverter.ToInt32(il, i + 1);
                if (((token >> 24) & 0xFF) != 0x06 && ((token >> 24) & 0xFF) != 0x0A && ((token >> 24) & 0xFF) != 0x2B) continue; // MethodDef, MemberRef, MethodSpec
                try
                {
                    MethodBase resolved = method.Module.ResolveMethod(token, method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null, null);
                    if (resolved != null && resolved == target) found.Add(i);
                }
                catch (System.Exception) { /* not a method token at this offset: the byte only looked like a call */ }
            }
            return found;
        }

        /// <summary>True when the named method (or its lambdas and state machine) calls a method that may live in another assembly (see CallOffsets).</summary>
        public static bool CallsAcrossAssemblies(System.Type owner, string methodName, MethodBase target)
        {
            string generated = "<" + methodName + ">";
            var types = new List<System.Type> { owner };
            types.AddRange(owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
            foreach (System.Type type in types)
            {
                bool wholeTypeIsTheMethod = type != owner && type.Name.StartsWith(generated);
                foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                {
                    if (!wholeTypeIsTheMethod && method.Name != methodName && !method.Name.StartsWith(generated)) continue;
                    if (CallOffsets(method, target).Count > 0) return true;
                }
            }
            return false;
        }

        private static List<int> OffsetsOf(MethodBase method, byte[] token, byte wanted)
        {
            var found = new List<int>();
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return found;
            for (int i = 0; i + 4 < il.Length; i++)
                if (il[i] == wanted && il[i + 1] == token[0] && il[i + 2] == token[1] && il[i + 3] == token[2] && il[i + 4] == token[3]) found.Add(i);
            return found;
        }

        private static bool Reads(MethodBase method, byte[] token)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                byte op = il[i];
                if (op != 0x28 && op != 0x6F && op != 0x73 && op != 0x7B) continue; // call, callvirt, newobj, ldfld
                if (il[i + 1] == token[0] && il[i + 2] == token[1] && il[i + 3] == token[2] && il[i + 4] == token[3]) return true;
            }
            return false;
        }
    }
}
