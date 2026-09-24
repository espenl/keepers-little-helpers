using System;
using System.Collections.Generic;
using System.Linq;

namespace KeepersJournal
{
    public sealed class BuildingFact
    {
        public string Id, Zone, Group;
        public bool Hidden, Temporary, Removing;
        public bool Present = true;
    }
    public sealed class BuildingCount
    {
        public int Built, InProgress;
        public string Caption
        {
            get
            {
                string built = Built > 0 ? "Built: " + Built : "";
                if (InProgress > 0) return built + (built.Length > 0 ? " | " : "") + "Building: " + InProgress;
                return built;
            }
        }
    }
    public static class JournalRules
    {
        public static string KnownScriptResult(string blueprint)
        {
            // Verified literal replacements/spawns in build 25506711, not executed scripts.
            switch (blueprint)
            {
                case "millstone_s": return "millstone";
                case "home_attic_s": return "ladder_home_first_floor";
                case "customization_enable_s": return "player_customization_after_fix";
                case "kitchen_oven_up_s": return "kitchen_oven_t2";
                case "kitchen_table_up_s": return "kitchen_table_t2";
                case "zombie_supplier_station_house_s": return "zombie_supplier_station_mini";
                default: return null;
            }
        }
        public static BuildingCount CountBuildings(IEnumerable<BuildingFact> objects, string zone, string resultId, string placementId)
        {
            var count = new BuildingCount();
            if (string.IsNullOrEmpty(zone) || string.IsNullOrEmpty(resultId)) return count;
            foreach (var item in objects)
            {
                if (item.Zone != zone || item.Hidden || item.Temporary || !item.Present) continue;
                // Planted and harvest-ready beds are different WGO IDs in the same native group.
                // Check construction first, so unfinished plots remain separate.
                if (placementId != resultId && item.Id == placementId) count.InProgress++;
                else if (item.Id == resultId
                    || (resultId == "garden_empty" && item.Group == "garden_bed"
                        && !string.IsNullOrEmpty(item.Id) && !item.Id.EndsWith("_place", StringComparison.Ordinal))
                    || (resultId == "vineyard_empty" && item.Group == "vineyard_objects"
                        && !string.IsNullOrEmpty(item.Id) && !item.Id.EndsWith("_place", StringComparison.Ordinal))) count.Built++;
            }
            return count;
        }
        public static List<string> MorningLines(IEnumerable<string> unlockedDays, int weekday,
            IDictionary<string, int> dayNumbers, bool sermonReady, bool boardKnown, bool boardReady,
            bool battleReady, bool resurrectionPower)
        {
            var unlocked = new HashSet<string>(unlockedDays ?? new string[0]);
            var lines = new List<string>();
            if (unlocked.Contains("day_wrath") && sermonReady)
                lines.Add("I can give a sermon today. Better remember my prayer!");
            if (boardKnown && unlocked.Contains("day_pride") && boardReady)
                lines.Add("The order board is open today. I should check it.");
            if (unlocked.Contains("day_sloth") && battleReady)
                lines.Add("The battle window is open today. I should check my preparations.");
            int expected;
            if (unlocked.Contains("day_envy") && dayNumbers.TryGetValue("day_envy", out expected) && weekday == expected && resurrectionPower)
                lines.Add("A storm day. I should check the resurrection equipment.");
            if (unlocked.Contains("day_lust") && dayNumbers.TryGetValue("day_lust", out expected) && weekday == expected)
                lines.Add("Today's the day to check for new arrivals.");
            if (unlocked.Contains("day_gluttony") && dayNumbers.TryGetValue("day_gluttony", out expected) && weekday == expected)
                lines.Add("I should check the panic-reduction machine today.");
            return lines;
        }
    }
    public sealed class MorningTracker
    {
        private int lastDay = -1;
        public void Load(int day, float time) { lastDay = time >= 0.25f ? day : day - 1; }
        public bool IsDue(int day, float time) { return day != lastDay && time >= 0.25f && time < 0.8f; }
        public void MarkShown(int day) { lastDay = day; }
        public void Reset() { lastDay = -1; }
    }
}
