using System.Text;
using UnityEngine;
using Overpower.Data;

namespace Overpower.Telemetry
{
    /// <summary>Builds the session header's "tuning" JSON object: every match-wide config, plus every
    /// weapon's and ability's own stat block, so a report built from an old match still reads
    /// correctly after Tudor retunes the game (the design doc's Principle 3).
    ///
    /// Uses JsonUtility.ToJson directly on the ScriptableObjects - it works the same in the Editor and
    /// in a build, unlike a reflection-heavy serializer such as Newtonsoft, which is why the runtime
    /// writes these strings as-is (TelemetryLine.Raw) rather than re-encoding them. Only the Editor's
    /// report builder (T5) re-parses this JSON, with Newtonsoft, to build its tables. If JsonUtility's
    /// output for some field (an object reference that doesn't serialize usefully, say) is not
    /// perfectly informative, that is still what gets written - see the T2 plan's own note.</summary>
    public static class TuningSnapshot
    {
        /// <summary>One flat JSON object: {"territory":{...},"gameplay":{...},"armor":{...},
        /// "weapons":[{"id":..,"name":..,"goldCost":..,"json":{...}}, ...],"abilities":[...]}.
        /// A null config/catalogue writes `null` (or `[]` for a catalogue) rather than throwing - a
        /// half-wired MatchTelemetry should still produce a parseable, if incomplete, session line.</summary>
        public static string Json(TerritoryConfig territoryConfig, GameplayConfig gameplayConfig,
                                   ArmorConfig armorConfig, WeaponCatalogue weapons, AbilityCatalogue abilities)
        {
            var sb = new StringBuilder(4096);
            sb.Append('{');

            AppendRawField(sb, "territory", territoryConfig != null ? JsonUtility.ToJson(territoryConfig) : "null");
            sb.Append(',');
            // GameplayConfig also carries the OverPower fields (Task 2.6) - they live on this asset,
            // not a separate one, so one JsonUtility.ToJson call already captures them (see the plan's
            // T2 step 4 note).
            AppendRawField(sb, "gameplay", gameplayConfig != null ? JsonUtility.ToJson(gameplayConfig) : "null");
            sb.Append(',');
            AppendRawField(sb, "armor", armorConfig != null ? JsonUtility.ToJson(armorConfig) : "null");
            sb.Append(',');
            AppendWeapons(sb, weapons);
            sb.Append(',');
            AppendAbilities(sb, abilities);

            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendRawField(StringBuilder sb, string key, string rawJson)
        {
            sb.Append('"').Append(key).Append("\":").Append(rawJson);
        }

        private static void AppendWeapons(StringBuilder sb, WeaponCatalogue weapons)
        {
            sb.Append("\"weapons\":[");
            if (weapons != null)
            {
                bool first = true;
                foreach (WeaponDefinition weapon in weapons.Weapons)
                {
                    if (weapon == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    AppendDefinitionEntry(sb, weapon.Id, weapon.DisplayName, weapon.GoldCost, JsonUtility.ToJson(weapon));
                }
            }
            sb.Append(']');
        }

        private static void AppendAbilities(StringBuilder sb, AbilityCatalogue abilities)
        {
            sb.Append("\"abilities\":[");
            if (abilities != null)
            {
                bool first = true;
                foreach (AbilityDefinition ability in abilities.Abilities)
                {
                    if (ability == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    AppendDefinitionEntry(sb, ability.Id, ability.DisplayName, ability.GoldCost, JsonUtility.ToJson(ability));
                }
            }
            sb.Append(']');
        }

        // definitionJson is JsonUtility's own output for the definition - already valid JSON, so it is
        // embedded as a raw nested value ("the runtime writes the raw JsonUtility strings as JSON
        // values" - the plan's own wording), not re-escaped into a string.
        private static void AppendDefinitionEntry(StringBuilder sb, int id, string name, int goldCost, string definitionJson)
        {
            sb.Append("{\"id\":").Append(id)
              .Append(",\"name\":").Append(EscapeJsonString(name))
              .Append(",\"goldCost\":").Append(goldCost)
              .Append(",\"json\":").Append(definitionJson)
              .Append('}');
        }

        // Only ever used for the two plain display-name strings above - everything else in this file
        // either comes from JsonUtility (already valid JSON) or is a plain integer.
        private static string EscapeJsonString(string value)
        {
            if (value == null) return "null";

            var sb = new StringBuilder(value.Length + 8);
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
