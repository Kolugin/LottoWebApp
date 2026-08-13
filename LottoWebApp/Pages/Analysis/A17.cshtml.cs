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
    public class A17Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A17Model(AppServices s)
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

        public List<FrequencyByDayOfMonthResult> Results { get; set; } = new();
        public Dictionary<string, List<FrequencyByDayOfMonthResult>> ResultsByField { get; set; } = new();

        public class FrequencyByDayOfMonthResult
        {
            public int DayOfMonth { get; set; }
            public int Number { get; set; }
            public int Count { get; set; }
            public double Chance { get; set; }
            public string Type { get; set; } = "";
        }

        public void Dispose()
        {
            Results?.Clear();
            ResultsByField?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A17_{Game}_{DrawCount}_{Direction}";
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
                var propNames = is320
                    ? new[] { $"{field}1", $"{field}2", $"{field}3" }
                    : first.GetType().GetProperties()
                        .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                        .OrderBy(p => int.Parse(p.Name.Substring(1)))
                        .Select(p => p.Name)
                        .ToArray();

                var dayOfMonthNumberCounts = new Dictionary<int, Dictionary<int, int>>();
                var dayOfMonthTotalDraws = new Dictionary<int, int>();

                foreach (var row in draws)
                {
                    var dateValue = dateProp.GetValue(row);
                    if (dateValue == null) continue;

                    if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;

                    int dayOfMonth = date.Day;
                    if (!dayOfMonthNumberCounts.ContainsKey(dayOfMonth))
                    {
                        dayOfMonthNumberCounts[dayOfMonth] = new Dictionary<int, int>();
                        dayOfMonthTotalDraws[dayOfMonth] = 0;
                    }

                    var numbers = propNames.Select(name =>
                    {
                        var prop = row.GetType().GetProperty(name);
                        if (prop == null) return 0;
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num != 0)
                            return num;
                        return 0;
                    }).Where(n => n != 0);

                    foreach (var number in numbers)
                    {
                        if (dayOfMonthNumberCounts[dayOfMonth].ContainsKey(number))
                            dayOfMonthNumberCounts[dayOfMonth][number]++;
                        else
                            dayOfMonthNumberCounts[dayOfMonth][number] = 1;
                    }
                    dayOfMonthTotalDraws[dayOfMonth]++;
                }

                var fieldResults = new List<FrequencyByDayOfMonthResult>();

                for (int day = 1; day <= 31; day++)
                {
                    if (!dayOfMonthNumberCounts.ContainsKey(day)) continue;

                    var numberCounts = dayOfMonthNumberCounts[day];
                    int totalDraws = dayOfMonthTotalDraws[day];
                    var sortedNumbers = numberCounts.OrderByDescending(n => n.Value).ToList();
                    var frequentNumbers = sortedNumbers.Take(10);
                    var rareNumbers = sortedNumbers.Skip(Math.Max(0, sortedNumbers.Count - 10)).Take(10);

                    foreach (var number in frequentNumbers)
                    {
                        double chance = Math.Round((double)number.Value / totalDraws, 4);
                        fieldResults.Add(new FrequencyByDayOfMonthResult
                        {
                            DayOfMonth = day,
                            Number = number.Key,
                            Count = number.Value,
                            Chance = chance,
                            Type = "Частое"
                        });
                    }
                    foreach (var number in rareNumbers)
                    {
                        double chance = Math.Round((double)number.Value / totalDraws, 4);
                        fieldResults.Add(new FrequencyByDayOfMonthResult
                        {
                            DayOfMonth = day,
                            Number = number.Key,
                            Count = number.Value,
                            Chance = chance,
                            Type = "Редкое"
                        });
                    }
                }

                if (is320)
                    ResultsByField[field] = fieldResults;
                else
                    Results = fieldResults;
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            return await _s.Csv17.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}