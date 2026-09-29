using System;
using System.IO;
using Overpower.Match;
using UnityEngine;

namespace Overpower.Net
{
    /// <summary>
    /// Task 9e (Tudor D21): who this install is, and which match it was last in. Two small text files next to the game
    /// (the parent of Application.dataPath - the build's own folder, or the project folder in the Editor; the same
    /// place the match logs go), with Application.persistentDataPath as the fallback when that folder cannot be written.
    ///
    /// A FILE, not PlayerPrefs: PlayerPrefs are shared by every copy of the game on the PC, and Photon refuses a second
    /// connection under the same user id in one room - two copies started from two folders (a playtest with several
    /// windows on one PC, the two-client test) must each be their own player. One id per install folder does that.
    ///
    /// The id is a random GUID made the first time the game runs. It is the Photon user id (what lets the server give a
    /// dropped player their own slot back), never the name and never the address. It is never logged in full: use
    /// <see cref="PlayerIdRule.ForLog"/>.
    /// </summary>
    public static class PlayerIdentity
    {
        public const string IdFileName = "overpower-player-id.txt";
        public const string LastMatchFileName = "overpower-last-match.json";

        private static string cachedId;

        /// <summary>The saved id, created and saved on first use.</summary>
        public static string UserId
        {
            get
            {
                if (!string.IsNullOrEmpty(cachedId))
                    return cachedId;

                // The file holds "id@tag" (machine + folder): a build folder copied elsewhere makes its own id.
                string tag = PlayerIdRule.Tag(SystemInfo.deviceName, Folder());
                string saved = TryRead(IdPath(), out bool readFailed);
                if (readFailed)
                {
                    // Never overwrite a file that exists but could not be read (it may hold the real id): a temporary id for this session.
                    cachedId = PlayerIdRule.NewId();
                    Debug.LogWarning("[REJOIN] the id file could not be read - using a temporary id for this session, the file is left alone");
                    return cachedId;
                }
                string id = PlayerIdRule.ResolveTagged(saved, tag, PlayerIdRule.NewId, out bool created);
                if (created)
                    TryWrite(IdPath(), PlayerIdRule.Compose(id, tag)); // file missing, made elsewhere (another folder or PC), or an untagged legacy id
                cachedId = id;
                return id;
            }
        }

        public static string IdPath() => Path.Combine(Folder(), IdFileName);

        public static string LastMatchPath() => Path.Combine(Folder(), LastMatchFileName);

        /// <summary>Test seam: forgets the cached id so the next read goes back to the file.</summary>
        public static void ForgetCachedId() => cachedId = null;

        // ---- the last match (RejoinStore's file) ----

        /// <summary>The saved record, or null when there is none or it cannot be read.</summary>
        public static RejoinRecord LoadLastMatch()
        {
            string text = TryRead(LastMatchPath());
            return RejoinRecord.TryParse(text, out RejoinRecord record) ? record : null;
        }

        /// <summary>Remembers "this player was in this room just now". Called on joining and every few seconds while
        /// playing, so a crash leaves it only a few seconds behind the moment the connection really dropped.</summary>
        public static void SaveLastMatch(string roomName, string nick)
        {
            if (string.IsNullOrEmpty(roomName))
                return;
            var record = new RejoinRecord
            {
                RoomName = roomName,
                UserId = UserId,
                Nick = nick ?? "",
                SavedAtMs = NowMs(),
            };
            TryWrite(LastMatchPath(), record.Serialize());
        }

        /// <summary>Forgets the saved match: the player chose a new one, or the rejoin found nothing to return to.</summary>
        public static void ClearLastMatch()
        {
            try
            {
                string path = LastMatchPath();
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[REJOIN] could not clear the saved match: {e.Message}");
            }
        }

        public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // ---- files ----

        private static string folder;

        private static string Folder()
        {
            if (folder != null)
                return folder;

            string game = null;
            try { game = Directory.GetParent(Application.dataPath)?.FullName; }
            catch (Exception) { /* falls through to the fallback */ }

            folder = game != null && CanWriteIn(game) ? game : Application.persistentDataPath;
            return folder;
        }

        private static bool CanWriteIn(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".overpower-write-test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string TryRead(string path) => TryRead(path, out _);

        private static string TryRead(string path, out bool failed)
        {
            failed = false;
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception e)
            {
                failed = true;
                Debug.LogWarning($"[REJOIN] could not read {Path.GetFileName(path)}: {e.Message}");
                return null;
            }
        }

        private static void TryWrite(string path, string text)
        {
            try { File.WriteAllText(path, text); }
            catch (Exception e)
            {
                Debug.LogWarning($"[REJOIN] could not write {Path.GetFileName(path)}: {e.Message}");
            }
        }
    }
}
