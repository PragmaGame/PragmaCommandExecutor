using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Walks the fields Unity serializes on tree preset nodes and their data: collects keys and copies data so that a
    /// built command never shares a mutable object with the asset.
    /// </summary>
    internal static class CommandTreePresetReflection
    {
        // Unity stops serializing nested plain objects at this depth.
        private const int MAX_DEPTH = 10;

        private static readonly Dictionary<Type, FieldInfo[]> Fields = new();
        private static readonly Func<object, object> MemberwiseCloneDelegate = (Func<object, object>)Delegate.CreateDelegate(
            typeof(Func<object, object>),
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic));

        /// <summary>
        /// A copy of <paramref name="data"/> that shares only Unity objects, strings and values Unity does not serialize.
        /// </summary>
        public static T CloneData<T>(T data)
        {
            return (T)Clone(data, 0);
        }

        public static IReadOnlyList<CommandTreePresetKeyInfo> CollectKeys(ICommandTreePresetNode root)
        {
            var keys = new List<CommandTreePresetKeyInfo>();
            CollectKeys(root, keys, 0);
            return keys;
        }

        /// <summary>
        /// The instance fields Unity serializes on <paramref name="type"/> and its base classes, base classes first.
        /// </summary>
        public static FieldInfo[] GetSerializedFields(Type type)
        {
            if (Fields.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var hierarchy = new List<Type>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                hierarchy.Add(current);
            }

            var fields = new List<FieldInfo>();
            for (var i = hierarchy.Count - 1; i >= 0; i--)
            {
                foreach (var field in hierarchy[i].GetFields(
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (IsSerialized(field))
                    {
                        fields.Add(field);
                    }
                }
            }

            var result = fields.ToArray();
            Fields[type] = result;
            return result;
        }

        private static bool IsSerialized(FieldInfo field)
        {
            if (field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), false))
            {
                return false;
            }

            return field.IsPublic
                   || field.IsDefined(typeof(SerializeField), false)
                   || field.IsDefined(typeof(SerializeReference), false);
        }

        private static void CollectKeys(object value, List<CommandTreePresetKeyInfo> keys, int depth)
        {
            if (value == null || depth > MAX_DEPTH || value is Object || value is string)
            {
                return;
            }

            if (value is ICommandTreePresetKey key)
            {
                var info = new CommandTreePresetKeyInfo(key.Name, key.ValueType);
                if (!keys.Contains(info))
                {
                    keys.Add(info);
                }

                return;
            }

            if (value is IList list)
            {
                foreach (var item in list)
                {
                    CollectKeys(item, keys, depth + 1);
                }

                return;
            }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || !IsSerializableObject(type))
            {
                return;
            }

            foreach (var field in GetSerializedFields(type))
            {
                CollectKeys(field.GetValue(value), keys, depth + 1);
            }
        }

        private static object Clone(object value, int depth)
        {
            if (value == null || depth > MAX_DEPTH || value is Object || value is string)
            {
                return value;
            }

            switch (value)
            {
                case AnimationCurve curve:
                    return new AnimationCurve(curve.keys)
                    {
                        preWrapMode = curve.preWrapMode,
                        postWrapMode = curve.postWrapMode,
                    };
                case Gradient gradient:
                    var copy = new Gradient { mode = gradient.mode, colorSpace = gradient.colorSpace };
                    copy.SetKeys(gradient.colorKeys, gradient.alphaKeys);
                    return copy;
                case Array array:
                    var arrayCopy = (Array)array.Clone();
                    for (var i = 0; i < arrayCopy.Length; i++)
                    {
                        arrayCopy.SetValue(Clone(arrayCopy.GetValue(i), depth + 1), i);
                    }

                    return arrayCopy;
                case IList list when value.GetType().IsGenericType
                                     && value.GetType().GetGenericTypeDefinition() == typeof(List<>):
                    var listCopy = (IList)Activator.CreateInstance(value.GetType(), list.Count);
                    foreach (var item in list)
                    {
                        listCopy.Add(Clone(item, depth + 1));
                    }

                    return listCopy;
            }

            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || !IsSerializableObject(type))
            {
                return value;
            }

            // A boxed struct is copied by the clone as well, so its fields can be replaced in place.
            var clone = MemberwiseCloneDelegate(value);
            foreach (var field in GetSerializedFields(type))
            {
                var fieldType = field.FieldType;
                if (fieldType.IsPrimitive || fieldType.IsEnum || fieldType == typeof(string))
                {
                    continue;
                }

                field.SetValue(clone, Clone(field.GetValue(clone), depth + 1));
            }

            return clone;
        }

        // A plain [Serializable] class or struct Unity would serialize field by field. Base class library types are
        // [Serializable] for the binary formatter (delegates, dictionaries) and are never copied field by field.
        private static bool IsSerializableObject(Type type)
        {
            var ns = type.Namespace;
            return type.IsDefined(typeof(SerializableAttribute), false)
                   && (ns == null || (ns != "System" && !ns.StartsWith("System.", StringComparison.Ordinal)))
                   && !typeof(Delegate).IsAssignableFrom(type);
        }
    }
}
