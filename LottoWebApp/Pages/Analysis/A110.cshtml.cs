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
    public class A110Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A110Model(AppServices s)
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

        public List<FrequencyByMonthDayWeekResult> Results { get; set; } = new();
        public Dictionary<string, List<FrequencyByMonthDayWeekResult>> ResultsByField { get; set; } = new();
        public Dictionary<string, Dictionary<string, List<FrequencyByMonthDayWeekResult>>> GroupedByField { get; set; } = new();

        public class FrequencyByMonthDayWeekResult
        {
            public string Month { get; set; } = "";
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
            string cacheKey = $"A110_{Game}_{DrawCount}_{Direction}";
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
            var dayOfWeekNames = new[] { "понедельник", "вторник", "среда", "четверг", "п€тница", "суббота", "воскресенье" };

            var monthOrder = new Dictionary<string, int>
            {
                {"€нварь", 1}, {"февраль", 2}, {"март", 3}, {"апрель", 4}, {"май", 5}, {"июнь", 6},
                {"июль", 7}, {"август", 8}, {"сент€брь", 9}, {"окт€брь", 10}, {"но€брь", 11}, {"декабрь", 12}
            };

            foreach (var field in fields)
            {
                var propNames = is320
                    ? new[] { $"{field}1", $"{field}2", $"{field}3" }
                    : first.GetType().GetProperties()
                        .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                        .OrderBy(p => int.Parse(p.Name.Substring(1)))
                        .Select(p => p.Name)
                        .ToArray();

                // ƒл€ детальной таблицы
                var dayMonthWeekNumberCounts = new Dictionary<string, Dictionary<int, int>>();
                var totalDrawsPerKey = new Dictionary<string, int>();

                // ƒл€ графиков: агрегаци€ по мес€цу
                var monthNumberCounts = new Dictionary<string, Dictionary<int, int>>();
                var totalDrawsPerMonth = new Dictionary<string, int>();

                foreach (var row in draws)
                {
                    var dateValue = dateProp.GetValue(row);
                    if (dateValue == null) continue;

                    if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;

                    string monthName = date.ToString("MMMM", culture).ToLower();
                    int dayOfMonth = date.Day;
                    string dayOfWeek = date.ToString("dddd", culture).ToLower();
                    var key = $"{monthName}-{dayOfMonth}-{dayOfWeek}";

                    if (!dayMonthWeekNumberCounts.TryGetValue(key, out var numberCounts))
                    {
                        numberCounts = new Dictionary<int, int>();
                        dayMonthWeekNumberCounts[key] = numberCounts;
                        totalDrawsPerKey[key] = 0;
                    }

                    if (!monthNumberCounts.TryGetValue(monthName, out var monthCounts))
                    {
                        monthCounts = new Dictionary<int, int>();
                        monthNumberCounts[monthName] = monthCounts;
                        totalDrawsPerMonth[monthName] = 0;
                    }

                    foreach (var propName in propNames)
                    {
                        var prop = row.GetType().GetProperty(propName);
                        if (prop == null) continue;

                        var val = prop.GetValue(row);
                        if (val == null) continue;

                        if (int.TryParse(val.ToString(), out int number) && number != 0)
                        {
                            if (numberCounts.ContainsKey(number)) numberCounts[number]++;
                            else numberCounts.Add(number, 1);
                            totalDrawsPerKey[key]++;

                            if (monthCounts.ContainsKey(number)) monthCounts[number]++;
                            else monthCounts.Add(number, 1);
                            totalDrawsPerMonth[monthName]++;
                        }
                    }
                }

                var fieldResults = new List<FrequencyByMonthDayWeekResult>();

                var sortedKeys = dayMonthWeekNumberCounts.Keys
                    .OrderBy(key =>
                    {
                        var parts = key.Split('-');
                        int monthNumber = monthOrder.ContainsKey(parts[0]) ? monthOrder[parts[0]] : 0;
                        int day = int.Parse(parts[1]);
                        int dayOfWeekIndex = Array.IndexOf(dayOfWeekNames, parts[2]);
                        return monthNumber * 1000000 + day * 10000 + dayOfWeekIndex;
                    }).ToList();

                foreach (var key in sortedKeys)
                {
                    var parts = key.Split('-');
                    var numberCounts = dayMonthWeekNumberCounts[key];
                    int totalDraws = totalDrawsPerKey[key];
                    if (totalDraws == 0) continue;

                    var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                    var frequent = sortedNumbers.Take(10);
                    var rare = sortedNumbers.Skip(Math.Max(0, sortedNumbers.Count - 10)).Take(10);

                    foreach (var num in frequent)
                    {
                        fieldResults.Add(new FrequencyByMonthDayWeekResult
                        {
                            Month = parts[0],
                            DayOfMonth = int.Parse(parts[1]),
                            DayOfWeek = parts[2],
                            Number = num.Key,
                            Count = num.Value,
                            Chance = Math.Round((double)num.Value / totalDraws, 4),
                            Type = "„астое"
                        });
                    }
                    foreach (var num in rare)
                    {
                        fieldResults.Add(new FrequencyByMonthDayWeekResult
                        {
                            Month = parts[0],
                            DayOfMonth = int.Parse(parts[1]),
                            DayOfWeek = parts[2],
                            Number = num.Key,
                            Count = num.Value,
                            Chance = Math.Round((double)num.Value / totalDraws, 4),
                            Type = "–едкое"
                        });
                    }
                }

                // јгрегированные данные дл€ графиков (по мес€цам)
                var aggregatedResults = new List<FrequencyByMonthDayWeekResult>();
                foreach (var monthName in monthNumberCounts.Keys.OrderBy(m => monthOrder.ContainsKey(m) ? monthOrder[m] : 0))
                {
                    var monthCounts = monthNumberCounts[monthName];
                    int totalDraws = totalDrawsPerMonth[monthName];
                    if (totalDraws == 0) continue;

                    var sortedNumbers = monthCounts.OrderByDescending(x => x.Value).ToList();
                    var frequent = sortedNumbers.Take(10);
                    var rare = sortedNumbers.Skip(Math.Max(0, sortedNumbers.Count - 10)).Take(10);

                    foreach (var num in frequent)
                    {
                        aggregatedResults.Add(new FrequencyByMonthDayWeekResult
                        {
                            Month = monthName,
                            DayOfMonth = 0,
                            DayOfWeek = "",
                            Number = num.Key,
                            Count = num.Value,
                            Chance = Math.Round((double)num.Value / totalDraws, 4),
                            Type = "„астое"
                        });
                    }
                    foreach (var num in rare)
                    {
                        aggregatedResults.Add(new FrequencyByMonthDayWeekResult
                        {
                            Month = monthName,
                            DayOfMonth = 0,
                            DayOfWeek = "",
                            Number = num.Key,
                            Count = num.Value,
                            Chance = Math.Round((double)num.Value / totalDraws, 4),
                            Type = "–едкое"
                        });
                    }
                }

                if (is320)
                {
                    ResultsByField[field] = fieldResults;
                    GroupedByField[field] = aggregatedResults
                        .GroupBy(r => r.Month)
                        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    Results = fieldResults;
                    GroupedByField[field] = aggregatedResults
                        .GroupBy(r => r.Month)
                        .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            return await _s.Csv110.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}