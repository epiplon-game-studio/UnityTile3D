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
    private Tile3D.Face brush = new Tile3D.Face() { Hidden = true };
    private bool draggingBlock;

    #region Colors
    private Color paintFaceColor = new Color(1, 0, 1, 0.2f);
    private Color paintOutlineColor = new Color(1, 0, 1);

    private Color hoverFaceColor = new Color(0, 0, 1f, 0.25f);
    private Color selectFaceColor = new Color(0, 0, 1f, 0.5f);
    private Color buildOutlineColor = Color.blue;
    #endregion

    private void OnSceneGUI()
    {
        DrawPanel();

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

    const int PAINTING_WINDOW_HEIGHT = 400;
    void DrawPanel()
    {
        Handles.BeginGUI();
        toolMode = (ToolModes)GUI.Toolbar(new Rect(10, 10, 200, 30), (int)toolMode, new[] { "Move", "Build", "Paint" });
        switch (toolMode)
        {
            case ToolModes.Building:
                GUI.Label(new Rect(10, 40, 200, 30), "Left-click to select tile.");
                GUI.Label(new Rect(10, 70, 200, 30), "Hold SHIFT for multiple selection.");
                break;
            case ToolModes.Painting:
                GUI.Window(0, new Rect(10, 70, 200, PAINTING_WINDOW_HEIGHT), PaintingWindow, "Tiles");
                break;
            default:
                break;
        }
        Handles.EndGUI();
    }

    #region Tool modes
    void BuildingMode()
    {
        Tools.current = Tool.None;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        Handles.color = Color.blue;
        hover = GetSelectionAt(e.mousePosition);

        // Draw only
        if (hover != null)
            DrawSelection(hover, hoverFaceColor, buildOutlineColor);

        if (selected != null && !selected.IsEmpty)
        {
            DrawSelection(selected, selectFaceColor, Color.clear);
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
        Tools.current = Tool.None;
        hover = GetSelectionAt(e.mousePosition);

        if (hover!= null)
        {
            var block = tiler.At(hover.Tile);
            if (block != null)
            {
                if (LeftButtonClick)
                {
                    // paint single tile
                    if (paintMode == PaintModes.Brush)
                    {
                        if (SetBlockFace(block, hover.Face, brush))
                            tiler.Rebuild(rebuildCollision: false);
                    }
                    // paint bucket
                    else if (paintMode == PaintModes.Fill)
                    {
                        var face = GetBlockFace(block, hover.Face);
                        if (FillBlockFace(block, face))
                            tiler.Rebuild(rebuildCollision: false);
                    }
                }
            }

            DrawSelection(hover, paintFaceColor, paintOutlineColor);
        }
    }

    Vector2 paintingScrollViewPos = Vector2.zero;
    void PaintingWindow(int id)
    {
        const int left = 10;
        const int width = 180;
        int zoom = 2;

        // paint mode
        paintMode = (PaintModes)GUI.Toolbar(new Rect(left, 25, width, 30), (int)paintMode, new[] { "Brush", "Fill" });
        brush.Rotation = GUI.Toolbar(new Rect(left + 50, 65, 130, 20), brush.Rotation, new[] { "0", "90", "180", "270" });
        brush.FlipX = GUI.Toggle(new Rect(left + 50, 90, 90, 20), brush.FlipX, "FLIP X");
        brush.FlipY = GUI.Toggle(new Rect(left + 115, 90, 90, 20), brush.FlipY, "FLIP Y");

        // empty tile
        if (DrawPaletteTile(new Rect(left, 65, 40, 40), null, brush.Hidden))
            brush.Hidden = true;

        // tiles
        if (tiler.Texture == null)
        {
            GUI.Label(new Rect(left, 120, width, 80), "Requires a Material\nwith a Texture");
        }
        else
        {
            var columns = tiler.Texture.width / tiler.TileWidth;
            var rows = tiler.Texture.height / tiler.TileHeight;
            //var tileWidth = width / columns;
            //var tileHeight = (tiler.TileHeight / (float)tiler.TileWidth) * tileWidth;
            var tileWidth = tiler.TileHeight * zoom;
            var tileHeight = tiler.TileWidth * zoom;

            var viewRect = new Rect(0, 0, columns * tileWidth, rows * tileHeight);
            paintingScrollViewPos = GUI.BeginScrollView(new Rect(5, 120, width+10, PAINTING_WINDOW_HEIGHT - 125), 
                paintingScrollViewPos, viewRect);

            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    var rect = new Rect(left + x * tileWidth, y * tileHeight, tileWidth, tileHeight);
                    var tile = new Vector2Int(x, rows - 1 - y);
                    if (DrawPaletteTile(rect, tile, brush.Tile == tile && !brush.Hidden))
                    {
                        brush.Tile = tile;
                        brush.Hidden = false;
                    }
                }
            }

            GUI.EndScrollView();
        }

        // repaint
        var e = Event.current;
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            Repaint();
    }

    bool DrawPaletteTile(Rect rect, Vector2Int? tile, bool selected)
    {
        var e = Event.current;
        var hover = !selected && e.mousePosition.x > rect.x && e.mousePosition.y > rect.y && e.mousePosition.x < rect.xMax && e.mousePosition.y < rect.yMax;
        var pressed = hover && e.type == EventType.MouseDown && e.button == 0;

        // hover
        if (hover)
            EditorGUI.DrawRect(rect, Color.yellow);
        // selected
        else if (selected)
            EditorGUI.DrawRect(rect, Color.blue);

        // tile
        if (tile.HasValue)
        {
            var coords = new Rect(tile.Value.x * tiler.UVTileSize.x, tile.Value.y * tiler.UVTileSize.y, tiler.UVTileSize.x, tiler.UVTileSize.y);
            GUI.DrawTextureWithTexCoords(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), tiler.Texture, coords);
        }
        else
        {
            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), Color.white);
            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 2, (rect.width - 4) / 2, (rect.height - 4) / 2), Color.gray);
            EditorGUI.DrawRect(new Rect(rect.x + 2 + (rect.width - 4) / 2, rect.y + 2 + (rect.height - 4) / 2, (rect.width - 4) / 2, (rect.height - 4) / 2), Color.gray);
        }

        if (pressed)
            e.Use();

        return pressed;
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

    private bool SetBlockFace(Tile3D.Block block, Vector3 normal, Tile3D.Face brush)
    {
        Undo.RecordObject(target, "SetBlockFaces");

        for (int i = 0; i < Tile3D.Faces.Length; i++)
        {
            if (Vector3.Dot(normal, Tile3D.Faces[i]) > 0.8f)
            {
                if (!brush.Hidden)
                {
                    if (brush != block.Faces[i])
                    {
                        block.Faces[i] = brush;
                        return true;
                    }
                }
                else if (!block.Faces[i].Hidden)
                {
                    block.Faces[i].Hidden = true;
                    return true;
                }
            }
        }

        return false;
    }
    private Tile3D.Face GetBlockFace(Tile3D.Block block, Vector3 face)
    {
        for (int i = 0; i < Tile3D.Faces.Length; i++)
        {
            if (Vector3.Dot(face, Tile3D.Faces[i]) > 0.8f)
                return block.Faces[i];
        }

        return block.Faces[0];
    }

    private bool FillBlockFace(Tile3D.Block block, Tile3D.Face face)
    {
        Vector3Int perp1, perp2;
        GetPerpendiculars(hover.Face, out perp1, out perp2);

        var active = new List<Tile3D.Block>();
        var filled = new HashSet<Tile3D.Block>();
        var directions = new Vector3Int[4] { perp1, perp1 * -1, perp2, perp2 * -1 };
        var outwards = hover.Face.Int();
        var changed = false;

        filled.Add(block);
        active.Add(block);
        SetBlockFace(block, hover.Face, brush);

        while (active.Count > 0)
        {
            var from = active[0];
            active.RemoveAt(0);

            for (int i = 0; i < 4; i++)
            {
                var next = tiler.At(from.Tile + directions[i]);
                if (next != null && !filled.Contains(next) && tiler.At(from.Tile + directions[i] + outwards) == null && GetBlockFace(next, hover.Face).Tile == face.Tile)
                {
                    filled.Add(next);
                    active.Add(next);
                    if (SetBlockFace(next, hover.Face, brush))
                        changed = true;
                }
            }
        }

        return changed;
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


