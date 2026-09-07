using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Snaps the editor's Game view to the shape the game is actually played in.
///
/// The court is 30 by 15 metres. On a portrait Game view the camera has to fit
/// that 2:1 lawn by its width, which leaves the whole game in a band across the
/// middle with empty surround above and below -- so the first thing anybody
/// sees is a picture of the wrong game.
///
/// Unity has no public API for the Game view's aspect: GameViewSizes,
/// GameViewSizeGroup and GameView are all internal. Hence the reflection. It is
/// wrapped so that a version where these names have moved reports that it could
/// not do it, rather than throwing -- this is a convenience, and the aspect can
/// always be set from the Game view's own dropdown.
/// </summary>
public static class GameViewAspect
{
    [MenuItem("Croquet/Game view — 1920 x 1080")]
    public static void FullHd() => Debug.Log(Apply("1920x1080", "FixedResolution", 1920, 1080));

    [MenuItem("Croquet/Game view — 16:9 landscape")]
    public static void Landscape() => Debug.Log(Apply("16:9", "AspectRatio", 16, 9));

    /// <summary>
    /// A fixed resolution rather than an aspect ratio, which is the fix for a
    /// game that looks low-resolution: an aspect ratio renders at whatever size
    /// the Game view window happens to be, so a small window is a small render
    /// blown up. A fixed size always renders at that size and is scaled down to
    /// fit, which is sharper on any window and is also what a device does.
    /// </summary>
    /// <returns>What happened, so a caller can log it.</returns>
    public static string Apply(string aspect, string kind = "AspectRatio",
                               int w = 16, int h = 9)
    {
        try
        {
            var editor = typeof(EditorWindow).Assembly;

            var sizesType = editor.GetType("UnityEditor.GameViewSizes");
            var groupType = editor.GetType("UnityEditor.GameViewSizeGroup");
            var sizeType = editor.GetType("UnityEditor.GameViewSize");
            var kindType = editor.GetType("UnityEditor.GameViewSizeType");
            var viewType = editor.GetType("UnityEditor.GameView");

            if (sizesType == null || groupType == null || sizeType == null ||
                kindType == null || viewType == null)
                return "Unity's Game view internals are not where they used to be.";

            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                                ?.GetValue(null);
            if (sizes == null) return "could not reach GameViewSizes.";

            var group = sizesType.GetProperty("currentGroup")?.GetValue(sizes);
            if (group == null) return "could not reach the current size group.";

            int index = IndexOf(groupType, group, sizeType, aspect);

            // Not every size group ships with every aspect, so add it if it is
            // not offered rather than giving up.
            if (index < 0)
            {
                var which = Enum.Parse(kindType, kind);
                var made = Activator.CreateInstance(sizeType, which, w, h, aspect);
                groupType.GetMethod("AddCustomSize")?.Invoke(group, new[] { made });
                index = IndexOf(groupType, group, sizeType, aspect);
            }

            if (index < 0) return "could not find or add a " + aspect + " size.";

            var window = EditorWindow.GetWindow(viewType, false, null, false);
            if (window == null) return "no Game view window is open.";

            var slot = viewType.GetProperty("selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (slot == null) return "GameView has no selectedSizeIndex any more.";

            slot.SetValue(window, index);
            window.Repaint();
            return "Game view set to " + aspect + " (slot " + index + ").";
        }
        catch (Exception e)
        {
            return "could not set the Game view: " + e.Message;
        }
    }

    static int IndexOf(Type groupType, object group, Type sizeType, string aspect)
    {
        int total = (int)(groupType.GetMethod("GetTotalCount")?.Invoke(group, null) ?? 0);
        var get = groupType.GetMethod("GetGameViewSize", new[] { typeof(int) });
        var text = sizeType.GetProperty("displayText");
        if (get == null || text == null) return -1;

        for (int i = 0; i < total; i++)
        {
            var shown = text.GetValue(get.Invoke(group, new object[] { i })) as string;
            if (shown != null && shown.Contains(aspect)) return i;
        }
        return -1;
    }
}
