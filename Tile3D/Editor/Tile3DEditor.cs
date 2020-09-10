using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Tile3D))]
public class Tile3DEditor : Editor
{
    public Tile3D tiler { get { return (Tile3D)target; } }
    public Vector3 origin { get { return tiler.transform.position; } }
    public Event e { get { return Event.current; } }

    // helpers
    public bool LeftButtonClick => e.type == EventType.MouseDown && e.button == 0;

    // current tool mode
    private ToolModes toolMode = ToolModes.Building;
    private PaintModes paintMode = PaintModes.Brush;

    // active selections
    private SingleSelection hover = null;
    private MultiSelection selected = new MultiSelection();
    private bool draggingBlock;

    private void OnSceneGUI()
    {

        Handles.BeginGUI();
        toolMode = (ToolModes)GUI.Toolbar(new Rect(10, 10, 200, 30), (int)toolMode, new[] { "Move", "Build", "Paint" });
        switch (toolMode)
        {
            case ToolModes.Transform:
                break;
            case ToolModes.Building:
                GUI.Label(new Rect(10, 40, 200, 30), "Left-click to select tile.");
                GUI.Label(new Rect(10, 70, 200, 30), "Hold SHIFT for multiple selection.");
                break;
            case ToolModes.Painting:
                break;
            default:
                break;
        }
        Handles.EndGUI();
        switch (toolMode)
        {
            case ToolModes.Transform:
                Tools.current = Tool.Move;
                break;
            case ToolModes.Building:
                BuildingMode();
                break;
            case ToolModes.Painting:
                PaintingMode();
                break;
            default:
                break;
        }

        // repaints the scene every time and keep interaction smooth
        SceneView.lastActiveSceneView.Repaint();
    }

    #region Tool modes
    void BuildingMode()
    {
        var e = Event.current;
        Tools.current = Tool.None;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        Handles.color = Color.blue;
        hover = GetSelectionAt(e.mousePosition);

        // Draw only
        if (hover != null)
            DrawSelection(hover, new Color(0, 0, 1f, 0.25f), Color.blue);

        if (selected != null && !selected.IsEmpty)
        {
            DrawSelection(selected, new Color(0, 0, 1f, 0.5f), Color.clear);
            EditorGUI.BeginChangeCheck();
            var start = CenterOfSelection(selected);
            var pulled = Handles.Slider(start, selected.Face * 0.5f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "Edit Mesh");
                MoveBlockAction(start, pulled);
            }
        }

        // only during hovering
        if (hover != null)
        {
            // mark tile as selected when clicked
            if (LeftButtonClick)
            {
                if (e.shift)
                {
                    var index = selected.Tiles.FindIndex(t => t == hover.Tile && selected.Face == hover.Face);
                    if (index >= 0)
                    {
                        selected.Tiles.RemoveAt(index);
                    }
                    else
                    {
                        if (selected.IsEmpty || hover.Face == selected.Face)
                            selected.Add(hover);
                        else
                        {
                            selected.Clear();
                            selected.Add(hover);
                        }
                    }
                }
                else
                {
                    selected.Clear();
                    selected.Add(hover);
                }

            }
        }
        else
        {
            if (LeftButtonClick)
                selected.Clear();
        }

        Selection.activeGameObject = tiler.transform.gameObject;
    }
    void PaintingMode()
    {

    }

    #endregion

    #region Actions
    void MoveBlockAction(Vector3 start, Vector3 pulled)
    {
        // get distance and direction
        var distance = (pulled - start).magnitude;
        var outwards = (int)Mathf.Sign(Vector3.Dot(pulled - start, selected.Face));

        using (TileProfiler.DragginBlocks.Auto())
        {
            // create or destroy a block (depending on direction)
            if (distance > 1f)
            {
                for (int i = 0; i < selected.Tiles.Count; i++)
                {
                    var tile = selected.Tiles[i];
                    var was = tile;
                    var next = tile + selected.Face.Int() * outwards;

                    if (outwards > 0)
                        tiler.Create(next, was);
                    else
                        tiler.Destroy(was);

                    selected.Tiles[i] = next;
                }

                tiler.Rebuild();
            }
        }
    }
    #endregion

    #region Selection
    // used to describe a selection (tile + face)
    private class SingleSelection
    {
        public Vector3Int Tile;
        public Vector3 Face;
    }

    private class MultiSelection
    {
        public List<Vector3Int> Tiles = new List<Vector3Int>();
        public Vector3 Face;
        public bool IsEmpty => Tiles.Count == 0;

        public void Add(SingleSelection selection)
        {
            if (IsEmpty)
                Face = selection.Face;

            Tiles.Add(selection.Tile);
        }

        public void Clear()
        {
            Tiles.Clear();
            Face = Vector3.zero;
        }
    }

    private SingleSelection GetSelectionAt(Vector2 mousePosition)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        var hits = Physics.RaycastAll(ray);

        foreach (var hit in hits)
        {
            var other = hit.collider.gameObject.GetComponent<Tile3D>();
            if (other == tiler)
            {
                var center = hit.point - hit.normal * 0.5f;

                return new SingleSelection()
                {
                    Tile = (center - origin).Floor(),
                    Face = hit.normal
                };
            }
        }

        return null;
    }


    private Vector3 CenterOfSelection(Vector3Int tile)
    {
        return origin + new Vector3(tile.x + 0.5f, tile.y + 0.5f, tile.z + 0.5f);
    }

    private Vector3 CenterOfSelection(SingleSelection selection)
    {
        return CenterOfSelection(selection.Tile);
    }

    private Vector3 CenterOfSelection(MultiSelection selection)
    {
        var median = Vector3.zero;
        foreach (var t in selection.Tiles)
            median += new Vector3(t.x + 0.5f, t.y + 0.5f, t.z + 0.5f);
        median /= selection.Tiles.Count;
        median += origin;

        return median;
    }

    #endregion

    #region Draw
    private void DrawSelection(SingleSelection selection, Color faceColor, Color outlineColor)
    {
        var center = CenterOfSelection(selection);
        DrawSelection(center, selection.Face, faceColor, outlineColor);
    }

    private void DrawSelection(MultiSelection selection, Color faceColor, Color outlineColor)
    {
        foreach (var tile in selection.Tiles)
            DrawSelection(CenterOfSelection(tile), selection.Face, faceColor, outlineColor);
    }

    private void DrawSelection(Vector3 center, Vector3 face, Color faceColor, Color outlineColor)
    {
        var front = center + face * 0.5f;
        Vector3 perp1, perp2;
        GetPerpendiculars(face, out perp1, out perp2);

        var a = front + (-perp1 + perp2) * 0.5f;
        var b = front + (perp1 + perp2) * 0.5f;
        var c = front + (perp1 + -perp2) * 0.5f;
        var d = front + (-perp1 + -perp2) * 0.5f;

        Handles.DrawSolidRectangleWithOutline(new Vector3[] { a, b, c, d }, faceColor, outlineColor);
    }

    private void GetPerpendiculars(Vector3 face, out Vector3 updown, out Vector3 leftright)
    {
        var up = (face.y == 0 ? Vector3.up : Vector3.right);
        updown = Vector3.Cross(face, up);
        leftright = Vector3.Cross(updown, face);
    }

    private void GetPerpendiculars(Vector3 face, out Vector3Int updown, out Vector3Int leftright)
    {
        Vector3 perp1, perp2;
        GetPerpendiculars(face, out perp1, out perp2);
        updown = perp1.Int();
        leftright = perp2.Int();
    }

    #endregion
}


