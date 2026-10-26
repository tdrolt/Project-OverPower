using System.Text;
using UnityEngine;
using Overpower.Data;

namespace Overpower.Telemetry
{
    /// <summary>Builds the session header's "tuning" JSON object: every match-wide config plus every weapon's and ability's stat
    /// block, so a report built from an old match still reads correctly after a retune (design doc Principle 3). JsonUtility.ToJson
    /// works the same in the Editor and a build (unlike reflection-heavy Newtonsoft), so the runtime writes these strings as-is
    /// (TelemetryLine.Raw); only the Editor's report builder re-parses the JSON, with Newtonsoft. Whatever JsonUtility writes for a
    /// field (an object reference that doesn't serialize usefully, say) is what gets written.</summary>
    public static class TuningSnapshot
    {
        /// <summary>One flat JSON object: {"territory":{...},"gameplay":{...},"armor":{...},
        /// "telemetry":{...},"weapons":[{"id":..,"name":..,"goldCost":..,"json":{...}}, ...],
        /// "abilities":[...]}. A null config/catalogue writes `null` (or `[]` for a catalogue) rather than throwing, so a half-wired
        /// MatchTelemetry still produces a parseable session line.</summary>
        public static string Json(TerritoryConfig territoryConfig, GameplayConfig gameplayConfig,
                                   ArmorConfig armorConfig, TelemetryConfig telemetryConfig,
                                   WeaponCatalogue weapons, AbilityCatalogue abilities)
        {
            var sb = new StringBuilder(4096);
            sb.Append('{');

            AppendRawField(sb, "territory", territoryConfig != null ? JsonUtility.ToJson(territoryConfig) : "null");
            sb.Append(',');
            // GameplayConfig also carries the OverPower fields, so this one ToJson call captures them.
            AppendRawField(sb, "gameplay", gameplayConfig != null ? JsonUtility.ToJson(gameplayConfig) : "null");
            sb.Append(',');
            AppendRawField(sb, "armor", armorConfig != null ? JsonUtility.ToJson(armorConfig) : "null");
            sb.Append(',');
            // The report aggregator's ResolveSampleIntervalSeconds reads tuning.telemetry.sampleIntervalSeconds (5s when absent).
            AppendRawField(sb, "telemetry", telemetryConfig != null ? JsonUtility.ToJson(telemetryConfig) : "null");
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

        // definitionJson is already valid JSON, so it is embedded as a raw nested value, not re-escaped into a string.
        private static void AppendDefinitionEntry(StringBuilder sb, int id, string name, int goldCost, string definitionJson)
        {
            sb.Append("{\"id\":").Append(id)
              .Append(",\"name\":").Append(EscapeJsonString(name))
              .Append(",\"goldCost\":").Append(goldCost)
              .Append(",\"json\":").Append(definitionJson)
              .Append('}');
        }

        // Only for the two display-name strings; everything else is JsonUtility output or an integer.
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
