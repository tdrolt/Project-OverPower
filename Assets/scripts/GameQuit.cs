using Photon.Pun;
using UnityEngine;

/// <summary>
/// Playtest extras P6 (2026-09-26): the one real "close the game" path, extracted out of
/// QuitButton.Quit so the Escape pop-up's Yes (QuitConfirmPanel) uses exactly the same order instead
/// of a second copy of the same three lines.
///
/// Order matters here (the brief's own "mind the order on quit" note): this zips THIS client's own
/// match log BEFORE disconnecting or quitting, rather than relying on MatchTelemetry's own
/// BeforeClose/OnApplicationQuit sequence to somehow still contain time to zip. MatchTelemetry.
/// FlushNow() (called inside MatchLogZip's own zip step) writes this client's buffered lines to disk
/// synchronously, so the zip this method builds is guaranteed to contain them - zipping AFTER the
/// writer's own Close() would work too (Close() flushes first), but doing it explicitly, first, here,
/// needs no assumption about how much of OnApplicationQuit's shutdown sequence Unity still lets user
/// code run in.
///
/// 2026-09-26 fix: always calls ZipNow(), not a "skip if the result panel already zipped this match"
/// variant - a real two-client check found that Disconnect() below can raise MatchTelemetry.OnLeftRoom
/// (clearing CurrentFolder) before this client's OWN OnApplicationQuit re-zip runs, and the OLD zip
/// name (a clock read, cached per match) then came out different between the two calls, leaving two
/// files. MatchLogZipRule.ZipFileName now names the zip after the match folder itself, which never
/// changes for the match, so calling ZipNow() twice just overwrites the same file - see MatchLogZip's
/// own comments (ZipNow, HandleBeforeClose, MatchLogZipRule.ResolveZipFolder) for the rest of the fix.
/// </summary>
public static class GameQuit
{
    public static void Quit()
    {
        Overpower.Telemetry.MatchLogZip.Instance?.ZipNow();

        // Leave the room first so the other players see us go immediately, instead of a ghost
        // player standing in the arena until Photon's timeout expires.
        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
