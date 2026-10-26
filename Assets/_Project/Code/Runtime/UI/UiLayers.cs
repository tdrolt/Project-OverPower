namespace Overpower.UI
{
    /// <summary>
    /// Every screen canvas's sorting order, in one table: higher draws on top and takes the click first. Tested as relations (UiLayersTests),
    /// and no canvas order is written as a bare number anywhere else, so a new panel is placed here, against its neighbours. Two orders are
    /// UiTheme fields (the scoreboard, the Dominion result card); their defaults live here. World-space canvases (name tags) have no order.
    /// </summary>
    public static class UiLayers
    {
        /// <summary>A light dark edge on the world while dead: under every panel, so it darkens the world and never the HUD.</summary>
        public const int DeathVignette = -30;
        /// <summary>The centre-scan countdown and the tower bounty: under the hit feedback, so a flash of damage draws over them.</summary>
        public const int ScanAndBounty = -21;
        public const int HitFeedback = -20;
        /// <summary>The HUD, the spectator bar, the warm-up bar and the Loadout (P) button.</summary>
        public const int Hud = -10;
        public const int Minimap = -9;
        /// <summary>RESPAWN IN n and the waiting card: over the HUD, under the Dominion HUD, the shop and the chat (it is placed clear of them).</summary>
        public const int DeathCard = -7;
        public const int DominionHud = -6;
        public const int Shop = -5;
        public const int ChatInMatch = -1;
        /// <summary>YOU WIN / YOU LOSE: over an open shop.</summary>
        public const int ResultPanels = 0;
        public const int SpectatorResult = 5;
        public const int DominionResultDefault = 5;
        public const int MatchLog = 10;
        public const int ScoreboardDefault = 30;
        public const int Lobby = 100;
        public const int ChatInLobbyRoom = 101;
        /// <summary>The F1 test range panel: a designer opened it on purpose.</summary>
        public const int TestRange = 500;
        /// <summary>The crosshair: over every panel it can show over. Every panel that takes a click shows the arrow instead
        /// (CrosshairRules.ShowCrosshair), so the crosshair never needs to sit over those.</summary>
        public const int Crosshair = 600;
        /// <summary>The Escape pop-up: a decision to close the game is never hidden behind anything but a lost connection.</summary>
        public const int QuitConfirm = 700;
        public const int ConnectionLost = 900;
    }
}
