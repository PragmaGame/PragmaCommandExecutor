using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Draws a <see cref="CommandTreePresetKey{T}"/> as one text field labelled with the type the key expects.
    /// </summary>
    [CustomPropertyDrawer(typeof(CommandTreePresetKey<>))]
    internal sealed class CommandTreePresetKeyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var name = property.FindPropertyRelative("_name");
            var keyLabel = new GUIContent($"{label.text} ({GetValueTypeName()})", label.tooltip);

            using (new EditorGUI.PropertyScope(position, keyLabel, property))
            {
                EditorGUI.PropertyField(position, name, keyLabel);
            }
        }

        private string GetValueTypeName()
        {
            var type = fieldInfo?.FieldType;
            if (type is { IsArray: true })
            {
                type = type.GetElementType();
            }
            else if (type is { IsGenericType: true } && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                type = type.GetGenericArguments()[0];
            }

            return type is { IsGenericType: true } ? Nicify(type.GetGenericArguments()[0]) : "?";
        }

        private static string Nicify(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.Name;
            }

            var name = type.Name;
            var tick = name.IndexOf('`');
            var arguments = string.Join(", ", Array.ConvertAll(type.GetGenericArguments(), Nicify));
            return $"{(tick < 0 ? name : name.Substring(0, tick))}<{arguments}>";
        }
    }
}
