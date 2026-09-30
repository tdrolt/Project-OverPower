using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Overpower.Telemetry;
using Overpower.EditorTools.Telemetry;

namespace Overpower.Tests
{
    /// <summary>Mark plan step 6: a `mark` key on `hit` lines (1 placed, 2 cashed, written only when
    /// non-zero - Decision 18) and the weapon table's marksPlaced/marksCashed counts. The first test
    /// pins the key's own shape through a standalone TelemetryLine, the same way TelemetryPhaseKeysTests
    /// pins `phase`/`elimination`/`adopt` - no Photon, no MonoBehaviour. The second uses a small
    /// hand-written temp-directory log (the same style TelemetryAggregatorReviewFixesTests uses, not
    /// the shared match_a fixture - see that file's own class comment), so this test's numbers can never
    /// be perturbed by an unrelated change to the big shared fixture.</summary>
    public class TelemetryMarkTests
    {
        [Test]
        public void HitLineCarriesTheMarkOnlyWhenThereIsOne()
        {
            Assert.AreEqual("mark", TelemetryKeys.Mark);
            Assert.AreNotEqual(TelemetryKeys.Mark, TelemetryKeys.Marker, "mark (the hit field) and marker (the event name) must never collide");

            var cashed = new TelemetryLine();
            cashed.Begin(TelemetryKeys.Hit, 5.5);
            cashed.Int(TelemetryKeys.Attacker, 1);
            cashed.Int(TelemetryKeys.Mark, 2);
            StringAssert.Contains("\"mark\":2", cashed.End());

            // Decision 18: written ONLY when non-zero - an ordinary hit line (no mark key touched at
            // all) must carry no "mark" substring whatsoever, so every hit line that predates this step
            // stays byte-identical.
            var ordinary = new TelemetryLine();
            ordinary.Begin(TelemetryKeys.Hit, 5.5);
            ordinary.Int(TelemetryKeys.Attacker, 1);
            StringAssert.DoesNotContain("mark", ordinary.End());
        }

        [Test]
        public void TheWeaponTableCountsMarksPlacedAndCashed()
        {
            string temp = Path.Combine(Path.GetTempPath(), "TelemetryMarkTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                // 4 weapon-12 hits alternating Applied(1)/Cashed(2)/Applied(1)/Cashed(2), plus one
                // weapon-11 hit that never touches a mark at all (no "mark" key on its own line) - the
                // exact shape mark step 4's own real weapon-12 sequence produces.
                string lines =
                    Session() +
                    Hit(t: 1, weapon: 12, mark: 1) +
                    Hit(t: 2, weapon: 12, mark: 2) +
                    Hit(t: 3, weapon: 12, mark: 1) +
                    Hit(t: 4, weapon: 12, mark: 2) +
                    Hit(t: 5, weapon: 11, mark: null);
                File.WriteAllText(Path.Combine(temp, "1.jsonl"), lines);

                var tables = TelemetryAggregator.Build(TelemetryLog.Load(temp));

                var row12 = tables.Weapons.Single(w => w.WeaponId == 12);
                Assert.AreEqual(2, row12.MarksPlaced, "two Applied(1) hits");
                Assert.AreEqual(2, row12.MarksCashed, "two Cashed(2) hits");

                var row11 = tables.Weapons.Single(w => w.WeaponId == 11);
                Assert.AreEqual(0, row11.MarksPlaced);
                Assert.AreEqual(0, row11.MarksCashed);
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        }

        private static string Session() =>
            "{\"e\":\"session\",\"t\":0,\"schema\":1,\"m\":\"M\",\"a\":1,\"nick\":\"n1\",\"tm\":0,\"master\":true," +
            "\"commit\":\"c\",\"uv\":\"u\",\"plat\":\"p\",\"tuning\":{}}\n";

        private static string Hit(double t, int weapon, int? mark) =>
            "{\"e\":\"hit\",\"t\":" + t + ",\"a\":1,\"at\":0,\"v\":2,\"vt\":1,\"w\":" + weapon +
            ",\"ab\":-1,\"src\":\"Projectile\",\"raw\":21,\"arm\":0,\"hpLost\":21,\"lethal\":false," +
            "\"d\":10,\"vul\":0,\"op\":false" + (mark.HasValue ? ",\"mark\":" + mark.Value : "") + "}\n";
    }
}
