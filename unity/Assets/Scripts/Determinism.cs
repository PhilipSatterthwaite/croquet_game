using Croquet.Core;
using UnityEngine;

/// <summary>
/// Asks this build whether it agrees with the pinned reference match.
///
/// The test suite runs on CoreCLR. The game runs on Mono in the editor and
/// IL2CPP on a device, and nothing about a green suite says those three arrive
/// at the same doubles. Online play that sends STROKES rather than positions
/// rests entirely on their agreeing, so this makes it a question that can be
/// answered rather than assumed.
///
/// It lives in the RUNTIME assembly rather than under Editor/ deliberately: the
/// answer that is still unknown is IL2CPP's, and IL2CPP only exists inside a
/// player on real hardware. An editor-only check can never reach it.
/// <see cref="DeterminismCheck"/> is the menu item that calls this, and
/// <see cref="StartMenu"/> logs it once on launch so `adb logcat` carries the
/// answer off a phone.
/// </summary>
public static class Determinism
{
    /// <summary>The result as a line of text, from wherever this is running.</summary>
    public static string Report()
    {
        var match = Reference.Play();
        bool agrees = match.Hash == Reference.Hash;

        var where = $"{Application.platform}, scripting {Backend()}";

        if (agrees)
            return $"Determinism OK on {where}: {match.Count} strokes, " +
                   $"0x{match.Hash:X16} as pinned.";

        return $"DETERMINISM MISMATCH on {where}: this build lands on " +
               $"0x{match.Hash:X16}, the pinned value is 0x{Reference.Hash:X16}. " +
               "Sending strokes between this build and one that disagrees would " +
               "drift; send positions, or find the divergence first.";
    }

    static string Backend()
    {
#if ENABLE_IL2CPP
        return "IL2CPP";
#else
        return "Mono";
#endif
    }
}
