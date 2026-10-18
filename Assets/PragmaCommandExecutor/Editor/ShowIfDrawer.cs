using UnityEditor;
using UnityEngine;

namespace Pragma.CommandExecutor
{
    /// <summary>
    /// Draws a field marked with <see cref="ShowIfAttribute"/> only while its condition holds, with its children.
    /// </summary>
    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    internal sealed class ShowIfDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // A hidden field also gives back the spacing the inspector adds before every field.
            return IsShown(property)
                ? EditorGUI.GetPropertyHeight(property, label, true)
                : -EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsShown(property))
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        private bool IsShown(SerializedProperty property)
        {
            var showIf = (ShowIfAttribute)attribute;
            var path = property.propertyPath;
            var separator = path.LastIndexOf('.');
            var conditionPath = separator < 0 ? showIf.Condition : path.Substring(0, separator + 1) + showIf.Condition;
            var condition = property.serializedObject.FindProperty(conditionPath);

            // A wrong condition name shows the field rather than hiding data.
            return condition is not { propertyType: SerializedPropertyType.Boolean } || condition.boolValue == showIf.Value;
        }
    }
}
