using System;
using System.Collections.Generic;
using System.Reflection;

namespace AdoFalling8K
{
    /// <summary>Shared game assembly lookup and reflection reads.</summary>
    internal static class GameReflection
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private static Assembly _assembly;
        private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();

        public static Assembly Assembly
        {
            get
            {
                if (_assembly == null)
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                        if (a.GetName().Name == "Assembly-CSharp") { _assembly = a; break; }
                return _assembly;
            }
        }

        public static Type FindType(Assembly assembly, params string[] names)
        {
            if (assembly == null) return null;
            foreach (string name in names)
            {
                Type cached;
                if (assembly == Assembly && Types.TryGetValue(name, out cached)) return cached;
            }
            foreach (Type type in assembly.GetTypes())
                foreach (string name in names)
                    if (type.Name == name)
                    {
                        if (assembly == Assembly) Types[name] = type;
                        return type;
                    }
            return null;
        }

        public static MemberInfo FindMember(Type type, params string[] names)
        { return Find(type, All, names); }
        public static MemberInfo FindStatic(Type type, string name)
        { return Find(type, All & ~BindingFlags.Instance, new[] { name }); }

        private static MemberInfo Find(Type type, BindingFlags flags, string[] names)
        {
            if (type == null) return null;
            foreach (string name in names)
            {
                PropertyInfo p = type.GetProperty(name, flags);
                if (p != null && p.CanRead) return p;
                FieldInfo f = type.GetField(name, flags);
                if (f != null) return f;
            }
            return null;
        }

        public static object Read(object target, MemberInfo member)
        {
            if (member == null) return null;
            PropertyInfo p = member as PropertyInfo;
            if (p != null)
                return p.CanRead && (target != null || p.GetGetMethod(true).IsStatic) ? p.GetValue(target, null) : null;
            FieldInfo f = member as FieldInfo;
            return f != null && (target != null || f.IsStatic) ? f.GetValue(target) : null;
        }

        public static object SafeRead(object target, MemberInfo member)
        { try { return Read(target, member); } catch { return null; } }
        public static object Resolve(MemberInfo member) { return SafeRead(null, member); }
        public static object ReadStatic(Type type, string name) { return Read(null, FindStatic(type, name)); }
        public static bool Write(object target, MemberInfo member, object value)
        {
            PropertyInfo p = member as PropertyInfo;
            if (p != null) { if (!p.CanWrite) return false; p.SetValue(target, value, null); }
            else { FieldInfo f = member as FieldInfo; if (f == null) return false; f.SetValue(target, value); }
            return true;
        }
        public static double ToDouble(object value)
        { try { return value == null ? 0.0 : Convert.ToDouble(value); } catch { return 0.0; } }
        public static bool ToBool(object value)
        { try { return value != null && Convert.ToBoolean(value); } catch { return false; } }
    }
}
