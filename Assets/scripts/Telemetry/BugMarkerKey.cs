using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Overpower.Data;
using Overpower.Net;
using Overpower.UI;
using Overpower.Weapons;

namespace Overpower.Telemetry
{
    /// <summary>
    /// Ctrl+B marks "a bug just happened" - owner-only, on every player prefab and always on: unlike the F1 test range panel
    /// (which GameplayConfig.TestRangeEnabled can switch off) a tester who switched F1 off must still be able to report a bug. A raw
    /// keyboard poll, like F1/Escape, not an InputAction: a tool key, never a rebindable gameplay control.
    ///
    /// EDITOR NOTE: Unity's own Editor binds Ctrl+B to File > Build And Run (ShortcutManagement).
    /// That binding only fires from the Editor's OWN window having focus, never from a Player build,
    /// and TelemetryConfig.BugMarkKey/BugMarkNeedsCtrl exist so the owner can move this key away from
    /// B for comfortable in-Editor testing, with no code change.
    /// </summary>
    [DisallowMultipleComponent]
    public class BugMarkerKey : MonoBehaviourPun
    {
        [Tooltip("Also where the mark key itself lives (Bug Mark Key, Bug Mark Needs Ctrl) - a " +
                 "designer can move it off B without touching code.")]
        [SerializeField] private TelemetryConfig config;

        [Tooltip("Read only for Bug Marked Text - the toast shown on a successful mark.")]
        [SerializeField] private UiTheme theme;

        // Wait between accepted marks (BugMarkRule). A plain constant, not a TelemetryConfig field.
        private const float MarkCooldownSeconds = 2f;

        private PlayerHealth playerHealth;
        private PlayerLifecycle lifecycle;
        private WeaponFiring weaponFiring;
        private AbilityRunner abilityRunner;
        private PlayerHud hud;

        // TeleportTo writes rb.position directly and transform.position lags until the next physics step (as in
        // PlayerTelemetry.MyPosition), so read through the Rigidbody.
        private Rigidbody body;
        private Vector3 MyPosition => body != null ? body.position : transform.position;

        private double lastMarkMatchTime = -1;

        private void Awake()
        {
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }

            playerHealth = GetComponent<PlayerHealth>();
            lifecycle = GetComponent<PlayerLifecycle>();
            abilityRunner = GetComponent<AbilityRunner>();
            hud = GetComponent<PlayerHud>();
            body = GetComponent<Rigidbody>();
            // Not on the root.
            weaponFiring = GetComponentInChildren<WeaponFiring>(true);

            if (playerHealth == null || lifecycle == null || weaponFiring == null || abilityRunner == null || hud == null)
                Debug.LogError($"[BugMarkerKey] {name}: missing PlayerHealth/PlayerLifecycle/WeaponFiring/AbilityRunner/PlayerHud - Ctrl+B cannot report a bug for this player.");
        }

        private void Update()
        {
            if (config == null)
                return;

            Keyboard kb = Keyboard.current;
            if (kb == null)
                return;

            if (config.BugMarkNeedsCtrl && !(kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
                return;

            KeyControl control = kb[config.BugMarkKey];
            if (control == null || !control.wasPressedThisFrame)
                return;

            TryMark();
        }

        private void TryMark()
        {
            if (MatchTelemetry.Instance == null || string.IsNullOrEmpty(MatchTelemetry.Instance.CurrentFolder))
                return; // No open file yet to save the screenshot next to.

            double now = MatchTelemetry.Instance.Now;
            if (!BugMarkRule.CanMark(PlayerInputRouter.IsTypingInChat(), now, lastMarkMatchTime, MarkCooldownSeconds))
                return;

            lastMarkMatchTime = now;

            int team = playerHealth != null ? playerHealth.TeamId : -1;
            bool alive = lifecycle == null || lifecycle.IsAlive;
            Vector3 pos = MyPosition;
            int zone = -1;
            BuildingManager.Instance?.TryGetZoneAt(pos, out zone);
            int weaponId = weaponFiring != null && weaponFiring.Weapon != null ? weaponFiring.Weapon.Id : LoadoutProperties.Empty;
            int attachmentId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Attachment) : LoadoutProperties.Empty;
            int mobilityId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Mobility) : LoadoutProperties.Empty;
            int ultimateId = abilityRunner != null ? abilityRunner.EquippedId(AbilitySlot.Ultimate) : LoadoutProperties.Empty;

            int actor = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            string fileName = $"bug_{actor}_{now.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}.png";
            string path = System.IO.Path.Combine(MatchTelemetry.Instance.CurrentFolder, fileName);

            try
            {
                ScreenCapture.CaptureScreenshot(path); // Absolute path: a relative one lands next to the exe.
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[BugMarkerKey] could not save the screenshot at '{path}': {e.Message}");
            }

            MatchTelemetry.Instance.LogBug(team, pos.x, pos.z, alive, zone, weaponId, attachmentId, mobilityId, ultimateId, fileName);

            if (hud != null && theme != null)
                hud.ShowToast(theme.bugMarkedText);
        }
    }
}
