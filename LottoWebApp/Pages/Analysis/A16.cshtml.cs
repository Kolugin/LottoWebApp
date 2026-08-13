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
    public class A16Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A16Model(AppServices s)
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

        public List<FrequencyByMonthResult> Results { get; set; } = new();
        public Dictionary<string, List<FrequencyByMonthResult>> ResultsByField { get; set; } = new();

        public class FrequencyByMonthResult
        {
            public string Month { get; set; } = "";
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
            string cacheKey = $"A16_{Game}_{DrawCount}_{Direction}";
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
                var monthCounts = new Dictionary<string, Dictionary<int, int>>();
                var monthTotal = new Dictionary<string, int>();

                var props = is320
                    ? new[] { $"{field}1", $"{field}2", $"{field}3" }
                    : first.GetType().GetProperties()
                        .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                        .Select(p => p.Name)
                        .ToArray();

                foreach (var row in draws)
                {
                    var dateValue = dateProp.GetValue(row);
                    if (dateValue == null) continue;

                    if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;

                    string monthKey = date.ToString("MMMM", CultureInfo.GetCultureInfo("ru-RU"));

                    if (!monthCounts.ContainsKey(monthKey))
                    {
                        monthCounts[monthKey] = new Dictionary<int, int>();
                        monthTotal[monthKey] = 0;
                    }

                    foreach (var propName in props)
                    {
                        var prop = row.GetType().GetProperty(propName);
                        if (prop == null) continue;

                        var val = prop.GetValue(row);
                        if (val == null) continue;

                        if (int.TryParse(val.ToString(), out int number) && number != 0)
                        {
                            if (monthCounts[monthKey].ContainsKey(number))
                                monthCounts[monthKey][number]++;
                            else
                                monthCounts[monthKey][number] = 1;

                            monthTotal[monthKey]++;
                        }
                    }
                }

                var sortedMonths = monthCounts.Keys
                    .Select(m =>
                    {
                        var monthDate = DateTime.ParseExact(m + " 2000", "MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")).Month;
                        return (MonthName: m, MonthNumber: monthDate);
                    })
                    .OrderBy(m => m.MonthNumber)
                    .Select(m => m.MonthName)
                    .ToList();

                var fieldResults = new List<FrequencyByMonthResult>();

                foreach (var month in sortedMonths)
                {
                    if (!monthCounts.ContainsKey(month) || !monthTotal.ContainsKey(month))
                        continue;

                    var numbers = monthCounts[month];
                    int total = monthTotal[month];
                    if (total == 0) continue;

                    var frequent = numbers.OrderByDescending(n => n.Value).Take(10).ToList();
                    var rare = numbers.OrderBy(n => n.Value).Take(10).ToList();

                    foreach (var (number, count) in frequent)
                    {
                        fieldResults.Add(new FrequencyByMonthResult
                        {
                            Month = month,
                            Number = number,
                            Count = count,
                            Chance = Math.Round((double)count / total, 4),
                            Type = "Частое"
                        });
                    }

                    foreach (var (number, count) in rare)
                    {
                        fieldResults.Add(new FrequencyByMonthResult
                        {
                            Month = month,
                            Number = number,
                            Count = count,
                            Chance = Math.Round((double)count / total, 4),
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
            return await _s.Csv16.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}