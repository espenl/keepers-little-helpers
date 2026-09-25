using System;
using System.Collections.Generic;
using System.Text;

namespace KeepersJournal
{
    public sealed class PinnedPlan
    {
        public string Id, Name;
        public int Quantity = 1;
        public int Batches = 1;
        public readonly Dictionary<string, int> Costs = new Dictionary<string, int>();
    }
    public static class HelperRules
    {
        public static Dictionary<string, int> Totals(IEnumerable<PinnedPlan> plans)
        {
            var totals = new Dictionary<string, int>();
            foreach (var plan in plans)
                foreach (var cost in plan.Costs)
                {
                    int old; totals.TryGetValue(cost.Key, out old);
                    totals[cost.Key] = (int)Math.Min(int.MaxValue, (long)old + Math.Max(0, cost.Value) * (long)Math.Max(0, Math.Min(99, plan.Quantity)));
                }
            return totals;
        }
        public static string PlainName(string value, int max)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "")
            {
                if (result.Length >= max) break;
                if (!char.IsControl(c) && c != '<' && c != '>') result.Append(c);
            }
            return result.ToString().Trim();
        }
        public static int TransferCount(int available, int capacity, bool matching, bool protectedItem, bool stackable)
        {
            return !matching || protectedItem || !stackable ? 0 : Math.Max(0, Math.Min(available, capacity));
        }
    }
}
