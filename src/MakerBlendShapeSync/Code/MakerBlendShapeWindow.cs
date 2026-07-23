using System.Collections.Generic;
using System.Linq;
using KKAPI.Maker;
using UnityEngine;

namespace MakerBlendShapeSync
{
    internal sealed class MakerBlendShapeWindow : MonoBehaviour
    {
        private ChaControl _chaCtrl;
        private BlendShapeSyncController _controller;
        private readonly List<SkinnedMeshRenderer> _renderers = new List<SkinnedMeshRenderer>();
        private SkinnedMeshRenderer _selectedRenderer;
        private Rect _windowRect = new Rect(120f, 80f, 760f, 620f);
        private int _windowId;
        private Vector2 _rendererScroll;
        private Vector2 _shapeScroll;
        private string _rendererSearch = "";
        private string _shapeSearch = "";
        private GUIStyle _selectedStyle;
        private GUIStyle _savedStyle;
        private int _rendererGeneration = -1;

        private void Awake()
        {
            enabled = false;
            DontDestroyOnLoad(this);
            _windowId = GUIUtility.GetControlID(FocusType.Passive);
        }

        private void OnEnable()
        {
            RefreshCharacter();
            RefreshRendererList();
        }

        private void OnGUI()
        {
            if (!MakerAPI.InsideMaker) { enabled = false; return; }
            if (_chaCtrl == null || _controller == null)
                RefreshCharacter();
            if (_chaCtrl == null || _controller == null) { enabled = false; return; }

            if (_rendererGeneration != _controller.RendererGeneration)
                RefreshRendererList();

            if (_selectedStyle == null)
            {
                _selectedStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
                _selectedStyle.normal.textColor = Color.cyan;
                _selectedStyle.hover.textColor = Color.cyan;
                _savedStyle = new GUIStyle(GUI.skin.button);
                _savedStyle.normal.textColor = Color.magenta;
                _savedStyle.hover.textColor = Color.magenta;
            }

            _windowRect = GUILayout.Window(_windowId, _windowRect, DrawWindow, "Maker Blend Shapes");
            BlendShapeUtilities.EatInputInRect(_windowRect);
        }

        private void RefreshCharacter()
        {
            _chaCtrl = MakerAPI.GetCharacterControl();
            _controller = _chaCtrl == null ? null : _chaCtrl.GetComponent<BlendShapeSyncController>();
        }

        private void RefreshRendererList()
        {
            _renderers.Clear();
            if (_chaCtrl == null) return;
            _renderers.AddRange(BlendShapeUtilities.EnumerateRenderers(_chaCtrl.transform)
                .Where(x => x != null && x.sharedMesh != null && x.sharedMesh.blendShapeCount > 0)
                .OrderBy(x => BlendShapeUtilities.GetRelativePath(_chaCtrl.transform, x.transform)));

            if (_selectedRenderer == null || !_renderers.Contains(_selectedRenderer))
                _selectedRenderer = _renderers.FirstOrDefault();
            _rendererGeneration = _controller == null ? -1 : _controller.RendererGeneration;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(70f))) RefreshRendererList();
            if (_selectedRenderer != null)
            {
                var scope = _controller.GetEffectiveTargetScope(_selectedRenderer);
                bool isBody = BlendShapeUtilities.IsBodyRenderer(_selectedRenderer);
                string scopeLabel = scope == BlendShapeTargetScope.Character
                    ? (isBody ? "Global body" : "Character")
                    : $"Coordinate {(_chaCtrl.fileStatus.coordinateType + 1)}";
                GUILayout.Label(scopeLabel, GUILayout.Width(105f));

                if (isBody)
                {
                    bool perCoordinate = _controller.IsRendererPerCoordinate(_selectedRenderer);
                    bool next = GUILayout.Toggle(perCoordinate, "Per-coordinate", GUILayout.Width(120f));
                    if (next != perCoordinate)
                        _controller.SetRendererPerCoordinate(_selectedRenderer, next);
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Apply", GUILayout.Width(60f))) _controller.ApplyCurrentCoordinate();
            if (GUILayout.Button("X", GUILayout.Width(26f)))
            {
                enabled = false;
                MakerBlendShapeSyncPlugin.MakerSidebarToggle?.SetValue(false, false);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawRendererPanel();
            DrawShapePanel();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 22f));
        }

        private void DrawRendererPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(260f), GUILayout.ExpandHeight(true));
            GUILayout.Label("Renderers");
            _rendererSearch = GUILayout.TextField(_rendererSearch);
            _rendererScroll = GUILayout.BeginScrollView(_rendererScroll);

            foreach (var renderer in _renderers)
            {
                string path = BlendShapeUtilities.GetRelativePath(_chaCtrl.transform, renderer.transform);
                string displayName = BlendShapeUtilities.GetDisplayName(_chaCtrl.transform, renderer);
                if (!Matches(path, _rendererSearch) && !Matches(renderer.name, _rendererSearch) && !Matches(displayName, _rendererSearch))
                    continue;

                bool isBody = BlendShapeUtilities.IsBodyRenderer(renderer);
                bool perCoordinate = isBody && _controller.IsRendererPerCoordinate(renderer);
                string label = displayName;
                label += $" ({renderer.sharedMesh.blendShapeCount})";
                if (isBody)
                    label += perCoordinate
                        ? $" [C{_chaCtrl.fileStatus.coordinateType + 1}]"
                        : " [G]";
                var style = renderer == _selectedRenderer ? _selectedStyle : GUI.skin.button;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(label, style, GUILayout.ExpandWidth(true)))
                    _selectedRenderer = renderer;
                if (isBody)
                {
                    bool next = GUILayout.Toggle(perCoordinate,
                        new GUIContent("", "Use separate BlendShape values for each coordinate"),
                        GUILayout.Width(18f));
                    if (next != perCoordinate)
                    {
                        _selectedRenderer = renderer;
                        _controller.SetRendererPerCoordinate(renderer, next);
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawShapePanel()
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandHeight(true));
            GUILayout.Label(_selectedRenderer == null ? "Blend Shapes" : BlendShapeUtilities.GetDisplayName(_chaCtrl.transform, _selectedRenderer));
            _shapeSearch = GUILayout.TextField(_shapeSearch);

            if (_selectedRenderer == null || _selectedRenderer.sharedMesh == null)
            {
                GUILayout.Box("", GUILayout.ExpandHeight(true));
                GUILayout.EndVertical();
                return;
            }

            string rendererPath = BlendShapeUtilities.GetRelativePath(_chaCtrl.transform, _selectedRenderer.transform);
            _shapeScroll = GUILayout.BeginScrollView(_shapeScroll);

            for (int i = 0; i < _selectedRenderer.sharedMesh.blendShapeCount; i++)
            {
                string shapeName = _selectedRenderer.sharedMesh.GetBlendShapeName(i);
                if (!Matches(shapeName, _shapeSearch))
                    continue;
                DrawShapeRow(rendererPath, i, shapeName);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawShapeRow(string rendererPath, int index, string shapeName)
        {
            var scope = _controller.GetEffectiveTargetScope(_selectedRenderer);
            int coordinate = scope == BlendShapeTargetScope.Character ? -1 : _chaCtrl.fileStatus.coordinateType;
            string meshName = _selectedRenderer.sharedMesh.name;
            var record = _controller.GetRecord(scope, coordinate, rendererPath, meshName, shapeName);
            bool saved = record != null;
            float current = _selectedRenderer.GetBlendShapeWeight(index);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(shapeName, saved ? _savedStyle : GUI.skin.label, GUILayout.Width(220f));
            GUILayout.Label(current.ToString("F1"), GUILayout.Width(48f));
            if (GUILayout.Button("Save", GUILayout.Width(48f)))
                SaveRecord(rendererPath, shapeName, current);
            if (GUILayout.Button("Del", GUILayout.Width(40f)))
            {
                _controller.RemoveRecord(_selectedRenderer, rendererPath, shapeName);
                _controller.ApplyCurrentCoordinate();
            }
            if (GUILayout.Button("0", GUILayout.Width(28f)))
            {
                _controller.CaptureBaseline(_selectedRenderer, shapeName);
                _selectedRenderer.SetBlendShapeWeight(index, 0f);
                SaveRecord(rendererPath, shapeName, 0f);
            }
            GUILayout.EndHorizontal();

            float next = GUILayout.HorizontalSlider(current, -100f, 200f);
            if (Mathf.Abs(next - current) > 0.001f)
            {
                _controller.CaptureBaseline(_selectedRenderer, shapeName);
                _selectedRenderer.SetBlendShapeWeight(index, next);
                SaveRecord(rendererPath, shapeName, next);
            }
            GUILayout.EndVertical();
        }

        private void SaveRecord(string rendererPath, string shapeName, float weight)
        {
            if (_selectedRenderer == null || _selectedRenderer.sharedMesh == null) return;
            _controller.CaptureBaseline(_selectedRenderer, shapeName);
            var scope = _controller.GetEffectiveTargetScope(_selectedRenderer);
            var record = new BlendShapeRecord
            {
                TargetScope = scope,
                Coordinate = scope == BlendShapeTargetScope.Character ? -1 : _chaCtrl.fileStatus.coordinateType,
                ShapeName = shapeName,
                Weight = weight
            };
            BlendShapeUtilities.PopulateRecordIdentity(_chaCtrl, _selectedRenderer, record);
            record.TargetScope = scope;
            record.Coordinate = scope == BlendShapeTargetScope.Character
                ? -1
                : _chaCtrl.fileStatus.coordinateType;
            if (scope == BlendShapeTargetScope.Character ||
                scope == BlendShapeTargetScope.CharacterCoordinate)
            {
                record.Slot = -1;
                record.SlotRelativePath = "";
            }
            _controller.UpsertRecord(record);
        }

        private static bool Matches(string value, string search)
        {
            return string.IsNullOrEmpty(search) ||
                   (!string.IsNullOrEmpty(value) && value.ToLowerInvariant().Contains(search.ToLowerInvariant()));
        }
    }
}
