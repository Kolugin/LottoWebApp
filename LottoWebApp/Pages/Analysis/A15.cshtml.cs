using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A15Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A15Model(AppServices s)
        {
            _s = s;
        }

        [BindProperty(SupportsGet = true)]
        public string? Game { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? DrawCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Direction { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedTable { get; set; }

        public List<FrequencyByWeekdayResult> Results { get; set; } = new();
        public Dictionary<string, List<FrequencyByWeekdayResult>> GroupedResults { get; set; } = new();

        // Для 320: данные по полям
        public Dictionary<string, List<FrequencyByWeekdayResult>> ResultsByField { get; set; } = new();
        public Dictionary<string, Dictionary<string, List<FrequencyByWeekdayResult>>> GroupedByField { get; set; } = new();

        public class FrequencyByWeekdayResult
        {
            public string DayOfWeek { get; set; } = "";
            public int Number { get; set; }
            public int Count { get; set; }
            public double Chance { get; set; }
            public string Type { get; set; } = "";
        }

        public void Dispose()
        {
            Results?.Clear();
            GroupedResults?.Clear();
            ResultsByField?.Clear();
            GroupedByField?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A15_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);
            bool isDesc = Direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (DrawCount.HasValue && DrawCount.Value > 0)
                query = query.Take(DrawCount.Value);

            var draws = await query.ToListAsync();
            if (!draws.Any()) return;

            var first = draws.First();
            var dateProp = first.GetType().GetProperty("Date");
            if (dateProp == null) return;

            bool is320 = Game.Equals("320", StringComparison.OrdinalIgnoreCase);
            var fields = is320 ? new[] { "B1", "B2" } : new[] { "B" };

            foreach (var field in fields)
            {
                var dayOfWeekCounts = new Dictionary<string, int[]>(StringComparer.InvariantCultureIgnoreCase);
                int maxNumber = is320 ? 20 : 60;

                foreach (var row in draws)
                {
                    var dateValue = dateProp.GetValue(row);
                    if (dateValue == null) continue;

                    if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;

                    string dayOfWeek = date.ToString("dddd", CultureInfo.GetCultureInfo("ru-RU"));

                    if (!dayOfWeekCounts.ContainsKey(dayOfWeek))
                        dayOfWeekCounts[dayOfWeek] = new int[maxNumber];

                    var props = is320
                        ? new[] { $"{field}1", $"{field}2", $"{field}3" }
                        : first.GetType().GetProperties()
                            .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                            .Select(p => p.Name)
                            .ToArray();

                    foreach (var propName in props)
                    {
                        var prop = row.GetType().GetProperty(propName);
                        if (prop == null) continue;

                        var val = prop.GetValue(row);
                        if (val == null) continue;

                        if (int.TryParse(val.ToString(), out int number) && number > 0 && number <= maxNumber)
                            dayOfWeekCounts[dayOfWeek][number - 1]++;
                    }
                }

                var fieldResults = new List<FrequencyByWeekdayResult>();

                foreach (var dayEntry in dayOfWeekCounts)
                {
                    string day = dayEntry.Key;
                    int[] counts = dayEntry.Value;
                    int totalCountForDay = counts.Sum();
                    if (totalCountForDay == 0) continue;

                    var frequent = counts.Select((cnt, idx) => new { Number = idx + 1, Count = cnt })
                        .Where(x => x.Count > 0).OrderByDescending(x => x.Count).Take(10);

                    var rare = counts.Select((cnt, idx) => new { Number = idx + 1, Count = cnt })
                        .Where(x => x.Count > 0).OrderBy(x => x.Count).Take(10);

                    foreach (var f in frequent)
                        fieldResults.Add(new FrequencyByWeekdayResult { DayOfWeek = day, Number = f.Number, Count = f.Count, Chance = Math.Round((double)f.Count / totalCountForDay, 4), Type = "Частое" });

                    foreach (var r in rare)
                        fieldResults.Add(new FrequencyByWeekdayResult { DayOfWeek = day, Number = r.Number, Count = r.Count, Chance = Math.Round((double)r.Count / totalCountForDay, 4), Type = "Редкое" });
                }

                if (is320)
                {
                    ResultsByField[field] = fieldResults;
                    GroupedByField[field] = fieldResults.GroupBy(r => r.DayOfWeek, StringComparer.InvariantCultureIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.InvariantCultureIgnoreCase);
                }
                else
                {
                    Results = fieldResults;
                    GroupedResults = fieldResults.GroupBy(r => r.DayOfWeek, StringComparer.InvariantCultureIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.InvariantCultureIgnoreCase);
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            return await _s.Csv15.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}