using System;
using UnityEngine;

namespace Pragma.CommandExecutor.Examples
{
    /// <summary>
    /// Draws a Play / Cancel panel for every <see cref="Example"/> in the scene and a caption above each target.
    /// Uses IMGUI so the examples do not depend on UI packages or on the input handling settings.
    /// </summary>
    public class ExamplesPanel : MonoBehaviour
    {
        private const float PANEL_WIDTH = 340f;
        private const float MARGIN = 10f;

        private Example[] _examples = Array.Empty<Example>();
        private Vector2Int _screenSize;
        private Vector2 _scroll;
        private GUIStyle _titleStyle;
        private GUIStyle _descriptionStyle;
        private GUIStyle _captionStyle;

        private void Start()
        {
            _examples = FindObjectsByType<Example>(FindObjectsSortMode.None);
            Array.Sort(_examples, (a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        }

        private void LateUpdate()
        {
            var screenSize = new Vector2Int(Screen.width, Screen.height);

            if (screenSize != _screenSize)
            {
                _screenSize = screenSize;
                FitCamera();
            }
        }

        // Frames every example in the part of the screen not covered by the panel, whatever the Game view aspect is.
        private void FitCamera()
        {
            var camera = Camera.main;

            if (camera == null || _examples.Length == 0)
            {
                return;
            }

            var bounds = new Bounds(_examples[0].transform.position, Vector3.zero);

            foreach (var example in _examples)
            {
                bounds.Encapsulate(example.transform.position);
            }

            // Room for the moves and the captions around the targets.
            bounds.Expand(new Vector3(4f, 5f, 0f));

            var freeWidth = Mathf.Clamp01(1f - (PANEL_WIDTH + 2f * MARGIN) / Screen.width);
            var tanHalfFov = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var distance = Mathf.Max(bounds.extents.y, bounds.extents.x / (camera.aspect * freeWidth)) / tanHalfFov;
            var viewWidth = 2f * distance * tanHalfFov * camera.aspect;

            // Shift the view so that the targets are centered in the free area to the right of the panel.
            var shift = (1f - freeWidth) * 0.5f * viewWidth;

            camera.orthographic = false;
            camera.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x - shift, bounds.center.y, bounds.center.z - distance),
                Quaternion.identity);
        }

        private void OnGUI()
        {
            CreateStyles();
            DrawCaptions();

            GUILayout.BeginArea(new Rect(MARGIN, MARGIN, PANEL_WIDTH, Screen.height - 2f * MARGIN), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            foreach (var example in _examples)
            {
                GUILayout.Label($"{example.Title}  <color=#9cf>[{example.Status}]</color>", _titleStyle);
                GUILayout.Label(example.Description, _descriptionStyle);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Play"))
                {
                    example.Play();
                }

                GUI.enabled = example.IsRunning;

                if (GUILayout.Button("Cancel"))
                {
                    example.Cancel();
                }

                GUI.enabled = true;
                GUILayout.EndHorizontal();

                example.DrawControls();
                GUILayout.Space(12f);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCaptions()
        {
            var camera = Camera.main;

            if (camera == null)
            {
                return;
            }

            foreach (var example in _examples)
            {
                if (example.Target == null)
                {
                    continue;
                }

                var point = camera.WorldToScreenPoint(example.transform.position + Vector3.down * 0.9f);

                if (point.z > 0f)
                {
                    GUI.Label(new Rect(point.x - 80f, Screen.height - point.y, 160f, 40f), example.Title, _captionStyle);
                }
            }
        }

        private void CreateStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label) { richText = true, fontStyle = FontStyle.Bold };
            _descriptionStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _captionStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = true };
        }
    }
}
