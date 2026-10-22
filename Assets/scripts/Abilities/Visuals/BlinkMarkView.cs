using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Overpower.Abilities
{
    /// <summary>
    /// What another player's screen shows of a Blink: a burst of grey triangle shards where the player left, and a flat grey hexagon ring
    /// snapping out on the floor where they landed, turned off the floor grid (BlinkLook decides every move and fade). Each shard is a
    /// two-point line narrowing to a point. The two marks are unparented objects, built once and reused, left where the blink happened.
    /// Whether each mark is drawn at all is the caller's sight check for its own spot, made before it plays.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlinkMarkView : MonoBehaviour
    {
        public const string ShardsName = "Blink Shards";
        public const string RingName = "Blink Ring";
        private const float ShardTilt = 25f; // tilts alternate shards up and down so the burst has depth; not a look number
        private const float FloorLift = 0.03f; // keeps the ring off the floor's own surface; not a look number
        private const int HexSides = 6;
        private const float FlatTopYaw = 30f; // half a side off the floor's pointy-topped hex tiles, so the ring never reads as a lit tile

        [SerializeField, Tooltip("Where the marks' colour (the Blink row of the look table), material and sizes come from: " +
                 "Assets/Gameplay/Config/UiTheme.asset and its Arena Look.")]
        private UiTheme theme;

        private AbilityModule module;
        private ArenaLook look;
        private LineRenderer[] shards;
        private LineRenderer ring;
        private Vector3 shardCentre;
        private Color shardColour;
        private Color ringColour;
        private float shardsAt = -1f;
        private float ringAt = -1f;
        private static bool warnedMissingLook;

        public GameObject Shards { get; private set; }
        public GameObject Ring { get; private set; }

        public void PlayDeparture(Vector3 point, int casterTeam)
        {
            if (!Ready())
                return;
            if (Shards == null)
                BuildShards();
            shardCentre = new Vector3(point.x, FloorUnder(point) + look.blinkShardHeight, point.z);
            shardColour = ColourFor(casterTeam);
            shardsAt = Time.time;
            DrawShards(0f);
            Shards.SetActive(true);
        }

        public void PlayArrival(Vector3 point, int casterTeam)
        {
            if (!Ready())
                return;
            if (Ring == null)
                BuildRing();
            Ring.transform.SetPositionAndRotation(new Vector3(point.x, FloorUnder(point) + FloorLift, point.z), Quaternion.Euler(90f, FlatTopYaw, 0f));
            ringColour = ColourFor(casterTeam);
            ringAt = Time.time;
            DrawRing(0f);
            Ring.SetActive(true);
        }

        /// <summary>The shards <paramref name="ageSeconds"/> after the blink: each flies out along its own direction, its point leading.</summary>
        public void DrawShards(float ageSeconds)
        {
            if (shards == null)
                return;
            float reach = BlinkLook.ShardReach(ageSeconds, look.blinkShardSeconds, look.blinkShardReach);
            Color shown = VisualTint.WithAlpha(shardColour, look.blinkAlpha * AbilityVisualGeometry.Fade01(ageSeconds, look.blinkShardSeconds));
            for (int i = 0; i < shards.Length; i++)
            {
                Vector3 direction = BlinkLook.ShardDirection(i, shards.Length, ShardTilt);
                Vector3 baseOfShard = shardCentre + direction * reach;
                shards[i].SetPosition(0, baseOfShard);
                shards[i].SetPosition(1, baseOfShard + direction * look.blinkShardLength);
                VisualTint.SetLineColor(shards[i], shown);
            }
        }

        /// <summary>The landing ring <paramref name="ageSeconds"/> after the blink: a flat, flat-topped hexagon that snaps out and keeps
        /// widening as it fades.</summary>
        public void DrawRing(float ageSeconds)
        {
            if (ring == null)
                return;
            VisualTint.FillFlatCircle(ring, BlinkLook.RingRadius(ageSeconds, look.blinkRingSnapSeconds, look.blinkRingFadeSeconds, look.blinkRingRadius, look.blinkRingSpread), HexSides);
            float alpha = look.blinkAlpha * BlinkLook.RingAlpha(ageSeconds, look.blinkRingSnapSeconds, look.blinkRingFadeSeconds);
            VisualTint.SetLineColor(ring, VisualTint.WithAlpha(ringColour, alpha));
        }

        private void Update()
        {
            if (shardsAt >= 0f && Shards != null)
            {
                float age = Time.time - shardsAt;
                if (age > look.blinkShardSeconds)
                {
                    Shards.SetActive(false);
                    shardsAt = -1f;
                }
                else
                    DrawShards(age);
            }
            if (ringAt >= 0f && Ring != null)
            {
                float age = Time.time - ringAt;
                if (age > look.blinkRingSnapSeconds + look.blinkRingFadeSeconds)
                {
                    Ring.SetActive(false);
                    ringAt = -1f;
                }
                else
                    DrawRing(age);
            }
        }

        private bool Ready()
        {
            look = theme != null ? theme.arenaLook : null;
            if (look != null && look.fxAlpha != null)
                return true;
            if (!warnedMissingLook)
            {
                warnedMissingLook = true;
                Debug.LogWarning($"[BlinkMarkView] {name}: no UiTheme Arena Look with FX Alpha - the Blink draws nothing on other screens.");
            }
            return false;
        }

        private Color ColourFor(int casterTeam)
        {
            if (module == null)
                module = GetComponent<AbilityModule>();
            int abilityId = module != null && module.Definition != null ? module.Definition.Id : -1;
            return LookColours.ForAbility(theme, abilityId, casterTeam, LookFamily.Movement);
        }

        private static float FloorUnder(Vector3 point) => GroundSnap.TryFindGroundY(point, out float groundY) ? groundY : point.y;

        private void BuildShards()
        {
            Shards = new GameObject(ShardsName);
            shards = new LineRenderer[look.blinkShardCount];
            for (int i = 0; i < shards.Length; i++)
            {
                LineRenderer shard = AddLine(Shards, "Shard " + i, look.blinkShardWidth);
                shard.useWorldSpace = true;
                shard.alignment = LineAlignment.View;
                shard.positionCount = 2;
                shard.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f); // a triangle: full width at its base, a point at its tip
                shards[i] = shard;
            }
        }

        private void BuildRing()
        {
            Ring = new GameObject(RingName);
            ring = AddLine(Ring, "Hex", look.blinkRingWidth);
            ring.alignment = LineAlignment.TransformZ; // with the root turned 90 degrees on X the band lies flat (VisualTint.FillFlatCircle)
            ring.numCornerVertices = 0;
        }

        private LineRenderer AddLine(GameObject parent, string lineName, float width)
        {
            var owner = new GameObject(lineName);
            owner.transform.SetParent(parent.transform, false);
            LineRenderer line = owner.AddComponent<LineRenderer>();
            line.sharedMaterial = look.fxAlpha;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void OnDestroy()
        {
            Free(Shards);
            Free(Ring);
            Shards = null;
            Ring = null;
        }

        private static void Free(GameObject go)
        {
            if (go == null)
                return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
