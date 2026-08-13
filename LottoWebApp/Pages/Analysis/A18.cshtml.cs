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
    public class A18Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A18Model(AppServices s)
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

        public List<FrequencyByYearResult> Results { get; set; } = new();
        public Dictionary<string, List<FrequencyByYearResult>> ResultsByField { get; set; } = new();

        public class FrequencyByYearResult
        {
            public int Year { get; set; }
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
            string cacheKey = $"A18_{Game}_{DrawCount}_{Direction}";
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

                var yearNumberCounts = new Dictionary<int, Dictionary<int, int>>();
                var yearTotalDraws = new Dictionary<int, int>();

                foreach (var row in draws)
                {
                    var dateValue = dateProp.GetValue(row);
                    if (dateValue == null) continue;

                    if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;

                    int year = date.Year;
                    if (!yearNumberCounts.ContainsKey(year))
                    {
                        yearNumberCounts[year] = new Dictionary<int, int>();
                        yearTotalDraws[year] = 0;
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
                        if (yearNumberCounts[year].ContainsKey(number))
                            yearNumberCounts[year][number]++;
                        else
                            yearNumberCounts[year][number] = 1;
                    }
                    yearTotalDraws[year]++;
                }

                var fieldResults = new List<FrequencyByYearResult>();

                foreach (var year in yearNumberCounts.Keys.OrderBy(y => y))
                {
                    var numberCounts = yearNumberCounts[year];
                    int totalDraws = yearTotalDraws[year];
                    var sortedNumbers = numberCounts.OrderByDescending(n => n.Value).ToList();
                    var frequentNumbers = sortedNumbers.Take(10);
                    var rareNumbers = sortedNumbers.Skip(Math.Max(0, sortedNumbers.Count - 10)).Take(10);

                    foreach (var number in frequentNumbers)
                    {
                        double chance = Math.Round((double)number.Value / totalDraws, 4);
                        fieldResults.Add(new FrequencyByYearResult
                        {
                            Year = year,
                            Number = number.Key,
                            Count = number.Value,
                            Chance = chance,
                            Type = "Частое"
                        });
                    }
                    foreach (var number in rareNumbers)
                    {
                        double chance = Math.Round((double)number.Value / totalDraws, 4);
                        fieldResults.Add(new FrequencyByYearResult
                        {
                            Year = year,
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
            return await _s.Csv18.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}