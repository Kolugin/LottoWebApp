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
    public class A21Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A21Model(AppServices s)
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

        public List<FrequencyResult> Results { get; set; } = new();
        public Dictionary<string, List<FrequencyResult>> ResultsByField { get; set; } = new();

        public class FrequencyResult
        {
            public int Number { get; set; }
            public double TotalFrequency { get; set; }
            public double Last10Frequency { get; set; }
            public double Last50Frequency { get; set; }
            public double Last100Frequency { get; set; }
        }

        public void Dispose()
        {
            Results?.Clear();
            ResultsByField?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A21_{Game}_{DrawCount}_{Direction}";
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
                        .Where(p =>
                        {
                            if (p.Name == "BB" && Game?.ToLower() == "blitz")
                                return false;
                            return p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _);
                        })
                        .OrderBy(p => int.Parse(p.Name.Substring(1)))
                        .Select(p => p.Name)
                        .ToArray();

                if (!propNames.Any()) continue;

                var ballCounts = new Dictionary<int, int>();
                var last10Counts = new Dictionary<int, int>();
                var last50Counts = new Dictionary<int, int>();
                var last100Counts = new Dictionary<int, int>();

                int totalRows = draws.Count;

                for (int drawIndex = 0; drawIndex < totalRows; drawIndex++)
                {
                    var currentRow = draws[drawIndex];
                    var currentNumbers = new List<int>();

                    foreach (var propName in propNames)
                    {
                        var prop = currentRow.GetType().GetProperty(propName);
                        if (prop == null) continue;

                        var val = prop.GetValue(currentRow);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num != 0)
                        {
                            currentNumbers.Add(num);
                        }
                    }

                    foreach (var number in currentNumbers)
                    {
                        if (ballCounts.ContainsKey(number))
                            ballCounts[number]++;
                        else
                            ballCounts[number] = 1;

                        if (drawIndex < 10) UpdateCounter(last10Counts, number);
                        if (drawIndex < 50) UpdateCounter(last50Counts, number);
                        if (drawIndex < 100) UpdateCounter(last100Counts, number);
                    }
                }

                int maxNumber = is320 ? 20 : ballCounts.Keys.Concat(last10Counts.Keys).Concat(last50Counts.Keys).Concat(last100Counts.Keys).DefaultIfEmpty(0).Max();

                var fieldResults = new List<FrequencyResult>();

                for (int ball = 1; ball <= maxNumber; ball++)
                {
                    int totalCount = ballCounts.TryGetValue(ball, out int cTotal) ? cTotal : 0;
                    int count10 = last10Counts.TryGetValue(ball, out int c10) ? c10 : 0;
                    int count50 = last50Counts.TryGetValue(ball, out int c50) ? c50 : 0;
                    int count100 = last100Counts.TryGetValue(ball, out int c100) ? c100 : 0;

                    if (totalCount == 0 && count10 == 0 && count50 == 0 && count100 == 0) continue;

                    double totalFreq = totalCount > 0 ? (double)totalRows / totalCount : 0;
                    double freq10 = count10 > 0 ? 10.0 / count10 : 0;
                    double freq50 = count50 > 0 ? 50.0 / count50 : 0;
                    double freq100 = count100 > 0 ? 100.0 / count100 : 0;

                    fieldResults.Add(new FrequencyResult
                    {
                        Number = ball,
                        TotalFrequency = Math.Round(totalFreq, 9),
                        Last10Frequency = Math.Round(freq10, 9),
                        Last50Frequency = Math.Round(freq50, 9),
                        Last100Frequency = Math.Round(freq100, 9)
                    });
                }

                if (is320)
                    ResultsByField[field] = fieldResults;
                else
                    Results = fieldResults;
            }
        }

        private void UpdateCounter(Dictionary<int, int> counter, int number)
        {
            if (counter.ContainsKey(number))
                counter[number]++;
            else
                counter[number] = 1;
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            return await _s.Csv21.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}