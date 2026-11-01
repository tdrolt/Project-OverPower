using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;

namespace Overpower.Vision
{
    /// <summary>
    /// What a team sees, for any team and with no local player: how a bot's brain asks "can my team see that spot / that body".
    /// The rules are TeamSight's (SightEyes, VisionRules, a Linecast on the Building layer at the eye's height) over the same room
    /// facts, without anything drawn. One eye list per team, rebuilt at most once a frame; every living member of the team is an
    /// eye, a human teammate included. Fog is the config's switch alone (a seat spectator host does not blind the bots).
    /// Configure comes first; unconfigured, or with the fog off, everything is seen.
    /// </summary>
    public static class TeamSightQuery
    {
        private sealed class TeamEyes
        {
            public int Frame = -1;
            public readonly List<Eye> Eyes = new List<Eye>(16);
        }

        private static readonly Dictionary<int, TeamEyes> byTeam = new Dictionary<int, TeamEyes>();
        private static readonly List<SightCandidate> candidates = new List<SightCandidate>(16);
        private static readonly System.Func<Eye, Vector2, bool> clearLine = ClearLine;
        private static VisionConfig current;
        private static float scopeAngle, scopeLength, scopeChange;
        private static int candidatesFrame = -1;
        private static int buildingMask = -1;
        private static float pointY; // the height of the point being asked about, read by ClearLine
        private static float feetOffset; // the capsule's bottom below the pivot, read once from a body
        private static bool feetOffsetRead;

        /// <summary>The sight numbers and the Scope's three sight numbers (ScopeAbility.SightConeAngleDegrees / SightConeLength /
        /// SightCircleChange), which every client reads the same. Forgets the eyes of an earlier match.</summary>
        public static void Configure(VisionConfig config, float scopeConeAngle, float scopeConeLength, float scopeCircleChange)
        {
            current = config;
            scopeAngle = scopeConeAngle;
            scopeLength = scopeConeLength;
            scopeChange = scopeCircleChange;
            candidatesFrame = -1;
            byTeam.Clear();
            feetOffsetRead = false;
        }

        private static bool FogOn => current != null && current.FogEnabled;

        /// <summary>True when the team sees this spot on the ground (the test is made Eye Height above it, as TeamSight.CanSee).
        /// True for everything when the fog is off.</summary>
        public static bool CanTeamSee(int team, Vector3 worldPoint)
        {
            if (!FogOn)
                return true;
            List<Eye> eyes = EyesOf(team);
            pointY = worldPoint.y;
            return VisionRules.TeamSeesWithEye(eyes, new Vector2(worldPoint.x, worldPoint.z), clearLine);
        }

        /// <summary>The team's own bodies are always seen; anyone else's only while their feet are (as TeamSight.CanSeePlayer).</summary>
        public static bool CanTeamSeeBody(int team, PhotonView body)
        {
            if (body == null || !FogOn)
                return true;
            if (team >= 0 && Teams.TryGetTeam(body.Owner, out int bodyTeam) && bodyTeam == team)
                return true;
            return CanTeamSee(team, body.transform.position + Vector3.up * FeetOffset(body));
        }

        private static List<Eye> EyesOf(int team)
        {
            if (!byTeam.TryGetValue(team, out TeamEyes slot))
                byTeam[team] = slot = new TeamEyes();
            if (slot.Frame == Time.frameCount)
                return slot.Eyes;
            slot.Frame = Time.frameCount;

            Gather();
            SightShape shape = VisionRules.ShapeFor(current.ConeAngleDegrees, current.ConeLength, current.CircleRadius, false, 0f, 0f, 0f);
            SightShape scopedShape = VisionRules.ShapeFor(current.ConeAngleDegrees, current.ConeLength, current.CircleRadius,
                true, scopeAngle, scopeLength, scopeChange);
            // Nobody is "local" here: Alive mode with the team as the viewer's makes every living member of it an eye.
            SightEyes.Build(candidates, team, ViewerMode.Alive, -1, shape, scopedShape, slot.Eyes);
            return slot.Eyes;
        }

        // Everyone in the room once a frame, for every team's list.
        private static void Gather()
        {
            if (candidatesFrame == Time.frameCount)
                return;
            candidatesFrame = Time.frameCount;
            candidates.Clear();
            Room room = PhotonNetwork.CurrentRoom;
            if (room == null)
                return;

            foreach (KeyValuePair<int, Photon.Realtime.Player> pair in room.Players)
            {
                Photon.Realtime.Player player = pair.Value;
                PhotonView view = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                if (view == null)
                    continue;
                if (!Teams.TryGetPlayingTeam(player, out int team))
                    team = -1; // a seat spectator or a player with no team yet: no team's eye
                bool? flag = player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool b ? b : (bool?)null;
                bool alive = PresenceRules.CountsAsAlive(Presence.IsDropped(player), flag);
                bool scoped = player.CustomProperties.TryGetValue(ScopeSightProperty.Key, out object scp) && ScopeSightProperty.Read(scp);
                Vector3 position = view.transform.position;
                Vector3 forward = view.transform.forward;
                float eyeY = position.y + FeetOffset(view) + current.EyeHeight;
                candidates.Add(new SightCandidate(team, alive, false,
                    new Vector2(position.x, position.z), new Vector2(forward.x, forward.z), eyeY, scoped));
            }
        }

        private static bool ClearLine(Eye eye, Vector2 to)
        {
            if (buildingMask < 0)
                buildingMask = LayerMask.GetMask("Building");
            Vector3 a = new Vector3(eye.Position.x, eye.EyeY, eye.Position.y);
            Vector3 b = new Vector3(to.x, ShotVisibilityRules.TargetHeight(false, eye.EyeY, pointY, current.EyeHeight), to.y);
            return !Physics.Linecast(a, b, buildingMask, QueryTriggerInteraction.Ignore);
        }

        // Every player body is the same prefab, so the first one asked decides.
        private static float FeetOffset(PhotonView body)
        {
            if (!feetOffsetRead)
            {
                feetOffsetRead = true;
                CapsuleCollider capsule = body.GetComponent<CapsuleCollider>();
                feetOffset = capsule != null ? (capsule.center.y - capsule.height * 0.5f) * body.transform.lossyScale.y : 0f;
            }
            return feetOffset;
        }
    }
}
