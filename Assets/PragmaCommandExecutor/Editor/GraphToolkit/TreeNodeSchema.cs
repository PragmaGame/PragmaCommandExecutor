using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pragma.CommandExecutor
{
    internal enum TreeNodePortKind
    {
        /// <summary>A <see cref="CommandTreePresetKey{T}"/>: a port of type T wired to a graph variable.</summary>
        Key,
        /// <summary>A list of <see cref="CommandTreePresetKey{T}"/>: a port of type T wired to any number of graph variables.</summary>
        KeyList,
        /// <summary>An <see cref="ICommandTreePresetNode"/>: a link port wired to the output of a node.</summary>
        Child,
        /// <summary>A list of <see cref="ICommandTreePresetNode"/>: an option with the count and as many link ports, in order.</summary>
        ChildList,
        /// <summary>Any other field: a typed port with an inline value, or wired to a constant.</summary>
        Value,
    }

    internal sealed class TreeNodePort
    {
        public TreeNodePort(TreeNodePortKind kind, string id, string displayName, Type dataType, Type fieldType,
            FieldInfo[] path, object defaultValue)
        {
            Kind = kind;
            Id = id;
            DisplayName = displayName;
            DataType = dataType;
            FieldType = fieldType;
            Path = path;
            DefaultValue = defaultValue;
        }

        public TreeNodePortKind Kind { get; }

        /// <summary>The port name, the field names joined by <see cref="TreeNodeSchema.SEPARATOR"/>; for
        /// <see cref="TreeNodePortKind.ChildList"/> the prefix of the port names and the option name.</summary>
        public string Id { get; }

        public string DisplayName { get; set; }

        /// <summary>The type of the port: the key value type, <see cref="Untyped"/> for links, or the field type.</summary>
        public Type DataType { get; }

        public Type FieldType { get; }

        /// <summary>The fields from the tree node to the field this port sets.</summary>
        public FieldInfo[] Path { get; }

        public object DefaultValue { get; }

        public bool IsKey => Kind is TreeNodePortKind.Key or TreeNodePortKind.KeyList;
        public bool IsLink => Kind is TreeNodePortKind.Child or TreeNodePortKind.ChildList;

        public string GetChildPortId(int index) => $"{Id}{TreeNodeSchema.SEPARATOR}{index}";
    }

    /// <summary>
    /// The ports and options of the graph node for an <see cref="ICommandTreePresetNode"/> type, read from the fields
    /// Unity serializes on it. Port names come from the field (or auto-property) names, so renaming a field disconnects
    /// its wires like it loses its serialized value.
    /// </summary>
    internal sealed class TreeNodeSchema
    {
        /// <summary>Joins the parts of a port name: Graph Toolkit reserves '.' for sub-ports.</summary>
        public const string SEPARATOR = "_";
        public const int MAX_CHILDREN = 32;
        public const int DEFAULT_CHILDREN = 2;
        private const int MAX_DEPTH = 7;

        private static readonly Dictionary<Type, TreeNodeSchema> Schemas = new();
        private static readonly HashSet<Type> UnityValueTypes = new()
        {
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Vector2Int), typeof(Vector3Int),
            typeof(Quaternion), typeof(Color), typeof(Color32), typeof(Rect), typeof(RectInt), typeof(Bounds),
            typeof(BoundsInt), typeof(LayerMask), typeof(Hash128), typeof(AnimationCurve), typeof(Gradient),
        };

        private readonly List<TreeNodePort> _ports = new();
        private readonly List<string> _errors = new();

        private TreeNodeSchema(Type treeNodeType)
        {
            TreeNodeType = treeNodeType;

            object defaults = null;
            try
            {
                defaults = Activator.CreateInstance(treeNodeType);
            }
            catch (Exception exception)
            {
                _errors.Add($"{treeNodeType.Name} cannot be created: {exception.Message}");
            }

            Walk(treeNodeType, defaults, new List<FieldInfo>(), string.Empty, 0);
            ResolveDisplayNames();
        }

        public Type TreeNodeType { get; }
        public IReadOnlyList<TreeNodePort> Ports => _ports;

        /// <summary>Fields the graph cannot edit.</summary>
        public IReadOnlyList<string> Errors => _errors;

        public static TreeNodeSchema Get(Type treeNodeType)
        {
            if (!Schemas.TryGetValue(treeNodeType, out var schema))
            {
                schema = new TreeNodeSchema(treeNodeType);
                Schemas[treeNodeType] = schema;
            }

            return schema;
        }

        public TreeNodePort FindPort(string portName)
        {
            foreach (var port in _ports)
            {
                if (port.Kind != TreeNodePortKind.ChildList && port.Id == portName)
                {
                    return port;
                }
            }

            foreach (var port in _ports)
            {
                if (port.Kind == TreeNodePortKind.ChildList
                    && portName.StartsWith(port.Id + SEPARATOR, StringComparison.Ordinal)
                    && int.TryParse(portName.Substring(port.Id.Length + SEPARATOR.Length), out _))
                {
                    return port;
                }
            }

            return null;
        }

        /// <summary>A stable description of the ports, hashed into the import dependency of the graphs.</summary>
        public string Describe()
        {
            return string.Join(";", _ports.Select(p => $"{p.Kind}:{p.Id}:{p.DataType.AssemblyQualifiedName}"));
        }

        /// <summary>
        /// The instance fields Unity serializes on <paramref name="type"/> and its base classes, base classes first.
        /// </summary>
        public static IEnumerable<FieldInfo> GetSerializedFields(Type type)
        {
            var hierarchy = new List<Type>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                hierarchy.Add(current);
            }

            for (var i = hierarchy.Count - 1; i >= 0; i--)
            {
                foreach (var field in hierarchy[i].GetFields(
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), false))
                    {
                        continue;
                    }

                    if (field.IsPublic || field.IsDefined(typeof(SerializeField), false)
                                       || field.IsDefined(typeof(SerializeReference), false))
                    {
                        yield return field;
                    }
                }
            }
        }

        public static bool TryGetKeyValueType(Type type, out Type valueType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(CommandTreePresetKey<>))
            {
                valueType = type.GetGenericArguments()[0];
                return true;
            }

            valueType = null;
            return false;
        }

        public static bool TryGetListElementType(Type type, out Type elementType)
        {
            if (type.IsArray && type.GetArrayRank() == 1)
            {
                elementType = type.GetElementType();
                return true;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                elementType = type.GetGenericArguments()[0];
                return true;
            }

            elementType = null;
            return false;
        }

        /// <summary>
        /// The name Unity shows for a field: <c>&lt;Duration&gt;k__BackingField</c> and <c>_duration</c> become
        /// <c>Duration</c> and <c>duration</c>.
        /// </summary>
        public static string GetMemberName(FieldInfo field)
        {
            var name = field.Name;
            if (name.StartsWith("<", StringComparison.Ordinal))
            {
                var end = name.IndexOf('>');
                if (end > 1)
                {
                    return name.Substring(1, end - 1);
                }
            }

            if (name.StartsWith("m_", StringComparison.Ordinal))
            {
                name = name.Substring(2);
            }

            return name.TrimStart('_');
        }

        private void Walk(Type type, object instance, List<FieldInfo> path, string idPrefix, int depth)
        {
            foreach (var field in GetSerializedFields(type))
            {
                var fieldType = field.FieldType;
                var memberName = GetMemberName(field);
                var id = idPrefix + memberName;
                var displayName = ObjectNames.NicifyVariableName(memberName);
                var value = instance != null ? field.GetValue(instance) : null;

                path.Add(field);
                var fieldPath = path.ToArray();

                if (TryGetKeyValueType(fieldType, out var keyType))
                {
                    _ports.Add(new TreeNodePort(TreeNodePortKind.Key, id, displayName, keyType, fieldType, fieldPath, null));
                }
                else if (TryGetListElementType(fieldType, out var elementType)
                         && TryGetKeyValueType(elementType, out var listKeyType))
                {
                    _ports.Add(new TreeNodePort(TreeNodePortKind.KeyList, id, displayName, listKeyType, fieldType, fieldPath, null));
                }
                else if (typeof(ICommandTreePresetNode).IsAssignableFrom(fieldType))
                {
                    _ports.Add(new TreeNodePort(TreeNodePortKind.Child, id, displayName, typeof(Untyped), fieldType,
                        fieldPath, null));
                }
                else if (TryGetListElementType(fieldType, out var childType) && typeof(ICommandTreePresetNode).IsAssignableFrom(childType))
                {
                    _ports.Add(new TreeNodePort(TreeNodePortKind.ChildList, id, displayName, typeof(Untyped),
                        fieldType, fieldPath, null));
                }
                else if (IsValueType(fieldType))
                {
                    _ports.Add(new TreeNodePort(TreeNodePortKind.Value, id, displayName, fieldType, fieldType, fieldPath, value));
                }
                else if (IsNestedObject(fieldType) && depth < MAX_DEPTH)
                {
                    Walk(fieldType, value, path, id + SEPARATOR, depth + 1);
                }
                else
                {
                    _errors.Add($"{TreeNodeType.Name}.{id}: a field of type {fieldType.Name} cannot be edited in the graph");
                }

                path.RemoveAt(path.Count - 1);
            }
        }

        private void ResolveDisplayNames()
        {
            // Nested fields show their own name ("From" rather than "Data From") unless two ports would read the same.
            foreach (var group in _ports.GroupBy(p => p.DisplayName).Where(g => g.Count() > 1))
            {
                foreach (var port in group)
                {
                    port.DisplayName = ObjectNames.NicifyVariableName(port.Id.Replace(SEPARATOR, string.Empty));
                }
            }
        }

        private static bool IsValueType(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(Object).IsAssignableFrom(type)
                   || UnityValueTypes.Contains(type);
        }

        private static bool IsNestedObject(Type type)
        {
            if (!type.IsDefined(typeof(SerializableAttribute), false) || typeof(Delegate).IsAssignableFrom(type)
                                                                             || type.IsArray || type.IsAbstract)
            {
                return false;
            }

            var ns = type.Namespace;
            return ns == null || (ns != "System" && !ns.StartsWith("System.", StringComparison.Ordinal));
        }
    }
}
