using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every match-wide number that is not specific to one weapon or one ability. One asset,
    /// edited in the Inspector, is the whole tuning surface for movement, health, respawning,
    /// the out-of-combat timer, overheat and the status caps.
    ///
    /// Fields are [SerializeField] private with read-only properties on purpose. A
    /// ScriptableObject is a single shared instance for the whole process, so writing to one at
    /// runtime quietly edits the asset in the Editor and does nothing at all in a build - the
    /// worst kind of bug, because it appears to work while you are testing. Read-only access
    /// makes that mistake impossible to make by accident; anything that needs to vary per player
    /// copies the value into a per-player runtime struct on spawn.
    ///
    /// Defaults live in the C# field initializers rather than being typed into the asset, so a
    /// freshly created asset already holds the shipping numbers and there is no window in which
    /// it silently holds zeros.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Gameplay Config")]
    public sealed class GameplayConfig : ScriptableObject
    {
        [Header("Movement")]
        [Tooltip("How fast a player walks with no buffs, slows or dashes applied, in metres per " +
                 "second. One world unit is one metre in this project, so 5 means a player " +
                 "crosses a 5 metre corridor in one second.")]
        [SerializeField] private float baseMoveSpeed = 5f;
        public float BaseMoveSpeed => baseMoveSpeed;

        [Header("Health")]
        [Tooltip("The health pool every player spawns with. Armor is a separate pool stacked on " +
                 "top of this one and is tuned in the Armor Config asset, so raising this does " +
                 "not change how much damage armor soaks.")]
        [SerializeField] private float maxHealth = 100f;
        public float MaxHealth => maxHealth;

        [Header("Respawn")]
        [Tooltip("How long you wait before respawning from your first death of the match.")]
        [SerializeField] private float respawnBaseSeconds = 5f;
        public float RespawnBaseSeconds => respawnBaseSeconds;

        [Tooltip("Added to the respawn wait for every death after the first. Your second death " +
                 "waits Base plus this, your third waits Base plus twice this, and so on - so " +
                 "dying repeatedly costs a player more and more time.")]
        [SerializeField] private float respawnPerDeathSeconds = 1f;
        public float RespawnPerDeathSeconds => respawnPerDeathSeconds;

        [Tooltip("The longest a respawn can ever take, however many times a player has died. " +
                 "Without this ceiling a late-game death would sideline someone for minutes.")]
        [SerializeField] private float respawnMaxSeconds = 10f;
        public float RespawnMaxSeconds => respawnMaxSeconds;

        [Tooltip("Fall below this Y height and you are returned to spawn, counted as a death. " +
                 "Set it a few metres under the lowest floor a player can legitimately stand " +
                 "on, so falling off the map ends quickly instead of falling forever.")]
        [SerializeField] private float killHeight = -10f;
        public float KillHeight => killHeight;

        [Header("Match start (2.7b)")]
        [Tooltip("Seconds between the match being started - by the third team's first player arriving, or by the " +
                 "host's Start button - and it going live. 'Match starts in N' shows on every screen meanwhile, and " +
                 "nothing counts yet. Going live is a fresh start: zones, gold, loadouts and respawn timers reset. " +
                 "0 = no countdown.")]
        [SerializeField, Min(0f)] private float matchStartCountdownSeconds = 5f;
        public float MatchStartCountdownSeconds => matchStartCountdownSeconds;

        [Header("Combat state")]
        [Tooltip("Seconds you must go without dealing or taking any damage before you count as " +
                 "out of combat. Three separate systems read this one timer: armor begins " +
                 "regenerating, the low-health passive switches on, and the shop gate uses its " +
                 "own shorter value below. That is why the game needs no event bus for an " +
                 "\"out of combat\" signal - all three readers already sit on the player.")]
        [SerializeField] private float outOfCombatSeconds = 6f;
        public float OutOfCombatSeconds => outOfCombatSeconds;

        [Tooltip("Seconds out of combat before the shop will let you buy anything. Deliberately " +
                 "shorter than the armor timer above so shopping between fights feels prompt, " +
                 "while still making it impossible to buy your way out of a fight you are losing.")]
        [SerializeField] private float shopOutOfCombatSeconds = 5f;
        public float ShopOutOfCombatSeconds => shopOutOfCombatSeconds;

        [Header("Overheat")]
        [Tooltip("The heat level at which a player is silenced and cannot fire until heat falls " +
                 "again. Divide this by a weapon's Overheat Per Shot to see how many shots that " +
                 "weapon gets before it locks up.")]
        [SerializeField] private float overheatMax = 100f;
        public float OverheatMax => overheatMax;

        [Tooltip("Seconds after the last shot before heat starts falling at all. Firing again " +
                 "restarts this wait, so tapping the trigger never cools you down.")]
        [SerializeField] private float overheatDecayDelay = 1.5f;
        public float OverheatDecayDelay => overheatDecayDelay;

        [Tooltip("How many heat points drain away each second once the decay delay has passed. " +
                 "At 25 a fully heated weapon sitting at 100 heat takes four seconds to clear.")]
        [SerializeField] private float overheatDecayPerSecond = 25f;
        public float OverheatDecayPerSecond => overheatDecayPerSecond;

        [Tooltip("The heat level at which the HUD starts warning the player. Keep it below " +
                 "Overheat Max so the warning arrives in time to act on, rather than landing " +
                 "at the same moment as the silence.")]
        [SerializeField] private float overheatWarningThreshold = 80f;
        public float OverheatWarningThreshold => overheatWarningThreshold;

        [Tooltip("Seconds into a silence before the Vent window opens - the extra beat you sit " +
                 "locked out before you get a chance to shorten it. 2 means the first two seconds " +
                 "of every silence are unskippable no matter how fast you react. Keep this at " +
                 "least as long as Overheat Decay Delay (1.5) - a hit only cuts the REMAINING " +
                 "lockout exactly in half once decay has actually started, which is guaranteed by " +
                 "the time the window can open when Vent Delay >= Overheat Decay Delay. Shorter " +
                 "still works, it just saves less: a hit landing before decay starts halves a " +
                 "clock that has not moved yet.")]
        [SerializeField, Min(0f)] private float ventDelay = 2f;
        public float VentDelay => ventDelay;

        [Tooltip("How long the Vent window stays open, in seconds, once it opens. Press R while " +
                 "it's open and the rest of your silence is cut in half; miss it and you serve the " +
                 "full lockout. 0 turns Vent off entirely - the window never opens, so R never hits.")]
        [SerializeField, Min(0f)] private float ventWindow = 0.8f;
        public float VentWindow => ventWindow;

        [Header("Status caps")]
        [Tooltip("The most a player can ever be slowed, counting every slow effect stacked " +
                 "together. 0.6 means 60 percent slower at worst, so no combination of slows " +
                 "can ever leave a player standing still and unable to escape.")]
        [SerializeField] private float slowCap = 0.60f;
        public float SlowCap => slowCap;

        [Tooltip("The most extra damage a player can ever take from vulnerability debuffs " +
                 "stacked together. 0.6 means 60 percent extra at worst, so piling on debuffs " +
                 "has a floor on how fast it can delete someone.")]
        [SerializeField] private float vulnerabilityCap = 0.60f;
        public float VulnerabilityCap => vulnerabilityCap;

        [Header("Abilities")]
        [Tooltip("Forgives a cast pressed up to this many seconds before its charge or cooldown " +
                 "actually returns, so a button mashed a frame early still fires instead of being " +
                 "silently dropped. Kept short on purpose - long enough to swallow one frame of " +
                 "timing jitter, too short to let a player queue up a surprise cast for the exact " +
                 "instant a silence ends. Applies to all three ability keys (right mouse, Space, " +
                 "Left Shift), not to the weapon.")]
        [SerializeField] private float abilityPressBufferSeconds = 0.12f;
        public float AbilityPressBufferSeconds => abilityPressBufferSeconds;

        [Header("Shop")]
        [Tooltip("On = everything is free all match, anywhere - test mode. Off = the warm-up before the match " +
                 "goes live is still a free sandbox (2.7b), and the real economy - prices, territory and " +
                 "out-of-combat rules, ultimate bought - starts the moment the match goes live.")]
        [SerializeField] private bool freeLoadout = false;
        public bool FreeLoadout => freeLoadout;

        [Tooltip("Fraction of what you paid that a weapon or armor reset refunds. 0.5 = half " +
                 "back, so re-speccing is possible without making it free to chop and change.")]
        [SerializeField, Range(0f, 1f)] private float sellRefundRate = 0.5f;
        public float SellRefundRate => sellRefundRate;

        [Header("OverPower (Task 2.6, GDD p.20)")]
        [Tooltip("Turn off to disable the OverPower comeback buff entirely for a playtest. Nothing " +
                 "arms, triggers or shows on the HUD.")]
        [SerializeField] private bool enableOverPower = true;
        public bool EnableOverPower => enableOverPower;

        [Tooltip("Seconds between two different enemy teams' hits for them to count as the same " +
                 "'attacked by both enemy teams' moment that arms the comeback buff. Too short and " +
                 "a real 3v1 gang-up would not register as simultaneous; too long and any two " +
                 "unrelated pokes minutes apart would arm it.")]
        [SerializeField, Min(0f)] private float overPowerWindowSeconds = 3f;
        public float OverPowerWindowSeconds => overPowerWindowSeconds;

        [Tooltip("How close, in metres, to the edge of a zone your team owns counts as 'near a " +
                 "territory you control' - both for a hit to arm the buff and for how far you may " +
                 "wander before an active or armed buff ends. 0 is inside the zone itself " +
                 "(BuildingManager.DistanceToOwnedZoneEdge already reports 0 there).")]
        [SerializeField, Min(0f)] private float overPowerRadius = 15f;
        public float OverPowerRadius => overPowerRadius;

        [Tooltip("The buff triggers the instant your health drops below this, while armed. The GDD's " +
                 "own number (p.20) - not a rate or a rescale, so it is not shared with any other " +
                 "health threshold in this asset.")]
        [SerializeField, Range(0f, 100f)] private float overPowerHealthThreshold = 35f;
        public float OverPowerHealthThreshold => overPowerHealthThreshold;

        [Tooltip("Fraction added to damage, fire rate and range while the buff is active - 0.10 = " +
                 "+10% on each, the GDD's own '3 of the highest parameters' number (p.20; damage, " +
                 "fire rate and range are Claude's own reading of 'highest parameters' for a " +
                 "prototype with no per-weapon stat ranking - see assumptions-for-tudor.md).")]
        [SerializeField, Range(0f, 1f)] private float overPowerStatBonus = 0.10f;
        public float OverPowerStatBonus => overPowerStatBonus;

        [Header("Debug")]
        [Tooltip("Turns the whole practice range and all of its dummy targets on or off in one " +
                 "click. Handy while tuning weapons; switch it off for a real match.")]
        [SerializeField] private bool testRangeEnabled = true;
        public bool TestRangeEnabled => testRangeEnabled;
    }
}
