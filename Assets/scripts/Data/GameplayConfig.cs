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

        [Header("Debug")]
        [Tooltip("Turns the whole practice range and all of its dummy targets on or off in one " +
                 "click. Handy while tuning weapons; switch it off for a real match.")]
        [SerializeField] private bool testRangeEnabled = true;
        public bool TestRangeEnabled => testRangeEnabled;
    }
}
