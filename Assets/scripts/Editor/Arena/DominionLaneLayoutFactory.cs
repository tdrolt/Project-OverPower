using System.Collections.Generic;
using Overpower.Data;
using UnityEditor;
using UnityEngine;

namespace Overpower.EditorTools
{
    /// <summary>
    /// Writes Assets/Gameplay/Config/DominionLaneLayout.asset from the numbers of Tudor's drawing (the canvas board "Dominion 2v2 map", Main.dc.html).
    /// The board is 12 pixels to the metre and its middle is (550, 342): every row below is the board's own left / top / width / height in pixels, and the
    /// conversion to metres is done here, once, so the asset holds metres and nobody types a converted number by hand. A pixel's "up" is the map's +Z.
    /// Menu: OverPower > Dominion > Write lane layout from the board.
    /// </summary>
    public static class DominionLaneLayoutFactory
    {
        public const string AssetPath = "Assets/Gameplay/Config/DominionLaneLayout.asset";

        private const float PixelsPerMetre = 12f;
        private const float BoardMiddleX = 550f;
        private const float BoardMiddleY = 342f;

        // left, top, width, height in board pixels
        private static readonly (string name, int l, int t, int w, int h)[] WallPixels =
        {
            ("Hall Wall Top",           401, 145, 298, 10),
            ("Hall Wall Bottom",        401, 529, 298, 10),
            ("Hall Stub Top Left",      401, 145,  10, 76),
            ("Hall Stub Bottom Left",   401, 463,  10, 76),
            ("Hall Stub Top Right",     689, 145,  10, 76),
            ("Hall Stub Bottom Right",  689, 463,  10, 76),
            ("Pocket Wall Left Top",     77, 211, 334, 10),
            ("Pocket Wall Left Bottom",  77, 463, 334, 10),
            ("Pocket Wall Left End",     77, 211,  10, 262),
            ("Pocket Wall Right Top",   689, 211, 334, 10),
            ("Pocket Wall Right Bottom",689, 463, 334, 10),
            ("Pocket Wall Right End",  1013, 211,  10, 262),
            ("H Wall Left",             443, 282,  10, 120),
            ("H Wall Right",            647, 282,  10, 120),
            ("H Arm Left",              448, 337,  66, 10),
            ("H Arm Right",             586, 337,  66, 10),
        };

        private static readonly (string name, int l, int t, int w, int h)[] BarrierPixels =
        {
            ("Plus Across", 514, 339, 72, 6),
            ("Plus Along",  547, 306,  6, 72),
        };

        // The grey 30-pixel squares: left, top.
        private static readonly (int l, int t)[] BoxPixels =
        {
            (271, 255), (271, 327), (271, 399), (337, 291), (337, 363),   // the left pocket: a row of 3 and a row of 2
            (799, 255), (799, 327), (799, 399), (733, 291), (733, 363),   // the right pocket
        };
        private const int BoxPixelSize = 30;

        // The zone circles: left, top (both 130 pixels across).
        private static readonly (int l, int t)[] ZonePixels = { (485, 157), (485, 397) };
        private const int ZonePixelSize = 130;

        // The spawn towers' circles: left, top (46 pixels across), and the dashed healing ground: left, top, width, height.
        private static readonly (int team, int towerL, int towerT, int healL, int healT, int healW, int healH)[] SpawnPixels =
        {
            (1, 155, 319, 82, 216, 189, 252),    // the left pocket is drawn purple: Purple (team 1)
            (0, 899, 319, 829, 216, 189, 252),   // the right pocket is drawn cyan, which 2v2 does not have: White (team 0)
        };
        private const int TowerPixelSize = 46;

        // Where players appear: this far from the tower, towards the middle of the map, so the nearest two spawn points of the teams are the 56 m of the title
        // (the towers themselves are 62 m apart on the drawing).
        private const float SpawnPointFromTowerMetres = 3f;

        // The inner faces of the outer walls, in board pixels, round the playable ground.
        private static readonly (int x, int y)[] OutlinePixels =
        {
            (411, 155), (689, 155), (689, 221), (1013, 221), (1013, 463), (689, 463),
            (689, 529), (411, 529), (411, 463), (87, 463), (87, 221), (411, 221),
        };

        [MenuItem("OverPower/Dominion/Write lane layout from the board")]
        public static void MenuWrite() => Write();

        public static DominionLaneLayout Write()
        {
            var layout = AssetDatabase.LoadAssetAtPath<DominionLaneLayout>(AssetPath);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<DominionLaneLayout>();
                AssetDatabase.CreateAsset(layout, AssetPath);
            }

            var walls = new List<LaneRect>();
            foreach (var w in WallPixels) walls.Add(Rect(w.name, w.l, w.t, w.w, w.h));
            var barriers = new List<LaneRect>();
            foreach (var b in BarrierPixels) barriers.Add(Rect(b.name, b.l, b.t, b.w, b.h));
            var boxes = new List<Vector2>();
            foreach (var b in BoxPixels) boxes.Add(Rect("", b.l, b.t, BoxPixelSize, BoxPixelSize).centre);
            var zones = new List<Vector2>();
            foreach (var z in ZonePixels) zones.Add(Rect("", z.l, z.t, ZonePixelSize, ZonePixelSize).centre);

            var spawns = new List<LaneSpawn>();
            foreach (var s in SpawnPixels)
            {
                Vector2 tower = Rect("", s.towerL, s.towerT, TowerPixelSize, TowerPixelSize).centre;
                LaneRect heal = Rect("", s.healL, s.healT, s.healW, s.healH);
                Vector2 towardsMiddle = new Vector2(-Mathf.Sign(tower.x), 0f);
                spawns.Add(new LaneSpawn
                {
                    team = s.team,
                    towerCentre = tower,
                    spawnPoint = tower + towardsMiddle * SpawnPointFromTowerMetres,
                    healCentre = heal.centre,
                    healSize = heal.size,
                });
            }

            var outline = new List<Vector2>();
            foreach (var p in OutlinePixels) outline.Add(new Vector2((p.x - BoardMiddleX) / PixelsPerMetre, -(p.y - BoardMiddleY) / PixelsPerMetre));

            layout.SetRows(walls, barriers, boxes, zones, spawns, outline, ZonePixelSize * 0.5f / PixelsPerMetre);
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
            return layout;
        }

        private static LaneRect Rect(string name, int l, int t, int w, int h)
        {
            float cx = (l + w * 0.5f - BoardMiddleX) / PixelsPerMetre;
            float cz = -(t + h * 0.5f - BoardMiddleY) / PixelsPerMetre;
            return new LaneRect(name, new Vector2(cx, cz), new Vector2(w / PixelsPerMetre, h / PixelsPerMetre));
        }
    }
}
