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
    public class A111Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A111Model(AppServices s)
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

        public List<PositionFrequencyResult> Results { get; set; } = new();
        public Dictionary<string, List<PositionFrequencyResult>> ResultsByField { get; set; } = new();

        public class PositionFrequencyResult
        {
            public string Position { get; set; } = "";
            public int Number { get; set; }
            public int Count { get; set; }
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            Results?.Clear();
            ResultsByField?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A111_{Game}_{DrawCount}_{Direction}";
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

                var positionNumberCounts = new Dictionary<int, Dictionary<int, int>>();
                var totalDrawsPerPosition = new int[propNames.Length];

                foreach (var row in draws)
                {
                    for (int i = 0; i < propNames.Length; i++)
                    {
                        var prop = row.GetType().GetProperty(propNames[i]);
                        if (prop == null) continue;

                        var val = prop.GetValue(row);
                        if (val == null) continue;

                        if (int.TryParse(val.ToString(), out int number) && number != 0)
                        {
                            if (!positionNumberCounts.TryGetValue(i + 1, out var numberCounts))
                            {
                                numberCounts = new Dictionary<int, int>();
                                positionNumberCounts[i + 1] = numberCounts;
                            }

                            if (numberCounts.ContainsKey(number))
                                numberCounts[number]++;
                            else
                                numberCounts.Add(number, 1);

                            totalDrawsPerPosition[i]++;
                        }
                    }
                }

                var fieldResults = new List<PositionFrequencyResult>();

                foreach (var position in positionNumberCounts.Keys.OrderBy(p => p))
                {
                    var numberCounts = positionNumberCounts[position];
                    int totalDraws = totalDrawsPerPosition[position - 1];
                    if (totalDraws == 0) continue;

                    var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();

                    foreach (var number in sortedNumbers)
                    {
                        double chance = Math.Round((double)number.Value / totalDraws, 4);
                        string positionName = $"Позиция {position}";
                        fieldResults.Add(new PositionFrequencyResult
                        {
                            Position = positionName,
                            Number = number.Key,
                            Count = number.Value,
                            Chance = chance
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
            return await _s.Csv111.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}