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
    public class A19Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A19Model(AppServices s)
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

        public List<FrequencyByDayMonthWeekResult> Results { get; set; } = new();
        public Dictionary<string, List<FrequencyByDayMonthWeekResult>> ResultsByField { get; set; } = new();

        // ƒл€ графиков: агрегированные данные по дн€м мес€ца (сумма всех дней недели)
        public Dictionary<string, Dictionary<int, List<FrequencyByDayMonthWeekResult>>> GroupedByField { get; set; } = new();

        public class FrequencyByDayMonthWeekResult
        {
            public int DayOfMonth { get; set; }
            public string DayOfWeek { get; set; } = "";
            public int Number { get; set; }
            public int Count { get; set; }
            public double Chance { get; set; }
            public string Type { get; set; } = "";
        }

        public void Dispose()
        {
            Results?.Clear();
            ResultsByField?.Clear();
            GroupedByField?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A19_{Game}_{DrawCount}_{Direction}";
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

            var culture = new CultureInfo("ru-RU");
            var daysOfWeek = new[] { "понедельник", "вторник", "среда", "четверг", "п€тница", "суббота", "воскресенье" };

            foreach (var field in fields)
            {
                var propNames = is320
                    ? new[] { $"{field}1", $"{field}2", $"{field}3" }
                    : first.GetType().GetProperties()
                        .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                        .OrderBy(p => int.Parse(p.Name.Substring(1)))
                        .Select(p => p.Name)
                        .ToArray();

                // ƒл€ детальной таблицы: день мес€ца + день недели
                var dayMonthWeekNumberCounts = new Dictionary<string, Dictionary<int, int>>();
                var totalDrawsPerKey = new Dictionary<string, int>();

                // ƒл€ графиков: только день мес€ца (агрегаци€ всех дней недели)
                var dayMonthNumberCounts = new Dictionary<int, Dictionary<int, int>>();
                var totalDrawsPerDay = new Dictionary<int, int>();

                // »нициализаци€
                for (int dayOfMonth = 1; dayOfMonth <= 31; dayOfMonth++)
                {
                    dayMonthNumberCounts[dayOfMonth] = new Dictionary<int, int>();
                    totalDrawsPerDay[dayOfMonth] = 0;

                    foreach (var dayOfWeek in daysOfWeek)
                    {
                        var key = $"{dayOfMonth}-{dayOfWeek}";
                        dayMonthWeekNumberCounts[key] = new Dictionary<int, int>();
                        totalDrawsPerKey[key] = 0;
                    }
                }

                foreach (var row in draws)
                {
                    var dateValue = dateProp.GetValue(row);
                    if (dateValue == null) continue;

                    if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;

                    int dayOfMonth = date.Day;
                    string dayOfWeek = date.ToString("dddd", culture.DateTimeFormat).ToLower();
                    var key = $"{dayOfMonth}-{dayOfWeek}";

                    foreach (var propName in propNames)
                    {
                        var prop = row.GetType().GetProperty(propName);
                        if (prop == null) continue;

                        var val = prop.GetValue(row);
                        if (val == null) continue;

                        if (int.TryParse(val.ToString(), out int number) && number != 0)
                        {
                            // ƒл€ детальной таблицы
                            if (dayMonthWeekNumberCounts[key].ContainsKey(number))
                                dayMonthWeekNumberCounts[key][number]++;
                            else
                                dayMonthWeekNumberCounts[key][number] = 1;
                            totalDrawsPerKey[key]++;

                            // ƒл€ графиков (агрегаци€ по дню мес€ца)
                            if (dayMonthNumberCounts[dayOfMonth].ContainsKey(number))
                                dayMonthNumberCounts[dayOfMonth][number]++;
                            else
                                dayMonthNumberCounts[dayOfMonth][number] = 1;
                            totalDrawsPerDay[dayOfMonth]++;
                        }
                    }
                }

                var fieldResults = new List<FrequencyByDayMonthWeekResult>();

                // ‘ормируем детальную таблицу (по дн€м недели)
                foreach (var entry in dayMonthWeekNumberCounts)
                {
                    var key = entry.Key;
                    var parts = key.Split('-');
                    int dayOfMonth = int.Parse(parts[0]);
                    string dayOfWeek = parts[1];
                    var numberCounts = entry.Value;
                    int totalDraws = totalDrawsPerKey[key];

                    if (totalDraws == 0) continue;

                    var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                    var frequent = sortedNumbers.Take(10);
                    var rare = sortedNumbers.Skip(Math.Max(0, sortedNumbers.Count - 10)).Take(10);

                    foreach (var num in frequent)
                    {
                        double chance = Math.Round((double)num.Value / totalDraws, 4);
                        fieldResults.Add(new FrequencyByDayMonthWeekResult
                        {
                            DayOfMonth = dayOfMonth,
                            DayOfWeek = dayOfWeek,
                            Number = num.Key,
                            Count = num.Value,
                            Chance = chance,
                            Type = "„астое"
                        });
                    }

                    foreach (var num in rare)
                    {
                        double chance = Math.Round((double)num.Value / totalDraws, 4);
                        fieldResults.Add(new FrequencyByDayMonthWeekResult
                        {
                            DayOfMonth = dayOfMonth,
                            DayOfWeek = dayOfWeek,
                            Number = num.Key,
                            Count = num.Value,
                            Chance = chance,
                            Type = "–едкое"
                        });
                    }
                }

                // ‘ормируем агрегированные данные дл€ графиков (по дн€м мес€ца)
                var aggregatedResults = new List<FrequencyByDayMonthWeekResult>();
                foreach (var entry in dayMonthNumberCounts)
                {
                    int dayOfMonth = entry.Key;
                    var numberCounts = entry.Value;
                    int totalDraws = totalDrawsPerDay[dayOfMonth];

                    if (totalDraws == 0) continue;

                    var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                    var frequent = sortedNumbers.Take(10);
                    var rare = sortedNumbers.Skip(Math.Max(0, sortedNumbers.Count - 10)).Take(10);

                    foreach (var num in frequent)
                    {
                        double chance = Math.Round((double)num.Value / totalDraws, 4);
                        aggregatedResults.Add(new FrequencyByDayMonthWeekResult
                        {
                            DayOfMonth = dayOfMonth,
                            DayOfWeek = "", // ѕусто, так как это агрегаци€
                            Number = num.Key,
                            Count = num.Value,
                            Chance = chance,
                            Type = "„астое"
                        });
                    }

                    foreach (var num in rare)
                    {
                        double chance = Math.Round((double)num.Value / totalDraws, 4);
                        aggregatedResults.Add(new FrequencyByDayMonthWeekResult
                        {
                            DayOfMonth = dayOfMonth,
                            DayOfWeek = "", // ѕусто, так как это агрегаци€
                            Number = num.Key,
                            Count = num.Value,
                            Chance = chance,
                            Type = "–едкое"
                        });
                    }
                }

                if (is320)
                {
                    ResultsByField[field] = fieldResults;
                    GroupedByField[field] = aggregatedResults
                        .GroupBy(r => r.DayOfMonth)
                        .ToDictionary(g => g.Key, g => g.ToList());
                }
                else
                {
                    Results = fieldResults;
                    GroupedByField[field] = aggregatedResults
                        .GroupBy(r => r.DayOfMonth)
                        .ToDictionary(g => g.Key, g => g.ToList());
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            return await _s.Csv19.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}