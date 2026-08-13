using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A22Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A22Model(AppServices s)
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
            string cacheKey = $"A22_{Game}_{DrawCount}_{Direction}";
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

                var allDraws = draws
                    .Select(row =>
                    {
                        var numbers = new List<int>();
                        foreach (var propName in propNames)
                        {
                            var prop = row.GetType().GetProperty(propName);
                            if (prop == null) continue;
                            var val = prop.GetValue(row);
                            if (val != null && int.TryParse(val.ToString(), out int num) && num != 0)
                            {
                                numbers.Add(num);
                            }
                        }
                        return numbers;
                    })
                    .ToList();

                int totalRows = allDraws.Count;

                var ballCounts = new Dictionary<int, int>();
                var freq10 = new Dictionary<int, FrequencyTracker>();
                var freq50 = new Dictionary<int, FrequencyTracker>();
                var freq100 = new Dictionary<int, FrequencyTracker>();

                try
                {
                    foreach (var draw in allDraws)
                    {
                        foreach (var number in draw)
                        {
                            if (!ballCounts.ContainsKey(number))
                                ballCounts[number] = 0;
                            ballCounts[number]++;
                        }
                    }

                    ProcessWindows(allDraws, 10, freq10);
                    ProcessWindows(allDraws, 50, freq50);
                    ProcessWindows(allDraws, 100, freq100);

                    var fieldResults = new List<FrequencyResult>();

                    foreach (var ball in ballCounts.Keys.OrderBy(k => k))
                    {
                        double totalFreq = (double)ballCounts[ball] / totalRows;
                        double most10 = freq10.TryGetValue(ball, out var t10) ? t10.GetMostFrequent() : 0;
                        double most50 = freq50.TryGetValue(ball, out var t50) ? t50.GetMostFrequent() : 0;
                        double most100 = freq100.TryGetValue(ball, out var t100) ? t100.GetMostFrequent() : 0;

                        fieldResults.Add(new FrequencyResult
                        {
                            Number = ball,
                            TotalFrequency = Math.Round(totalFreq, 9),
                            Last10Frequency = Math.Round(most10, 9),
                            Last50Frequency = Math.Round(most50, 9),
                            Last100Frequency = Math.Round(most100, 9)
                        });
                    }

                    if (is320)
                        ResultsByField[field] = fieldResults;
                    else
                        Results = fieldResults;
                }
                finally
                {
                    ballCounts.Clear();
                    freq10.Clear();
                    freq50.Clear();
                    freq100.Clear();
                    allDraws.Clear();
                }
            }
        }

        private void ProcessWindows(List<List<int>> allDraws, int windowSize, Dictionary<int, FrequencyTracker> frequencyTracker)
        {
            int totalDraws = allDraws.Count;
            if (totalDraws < windowSize) return;

            Span<int> counts = stackalloc int[100];
            HashSet<int> activeNumbers = new HashSet<int>();

            for (int i = 0; i < windowSize; i++)
            {
                foreach (int num in allDraws[i])
                {
                    if (counts[num]++ == 0)
                        activeNumbers.Add(num);
                }
            }

            foreach (int num in activeNumbers)
            {
                if (!frequencyTracker.TryGetValue(num, out var tracker))
                    frequencyTracker[num] = tracker = new FrequencyTracker();

                double freq = (double)windowSize / counts[num];
                tracker.Add(freq);
            }

            for (int i = windowSize; i < totalDraws; i++)
            {
                foreach (int num in allDraws[i - windowSize])
                {
                    if (--counts[num] == 0)
                        activeNumbers.Remove(num);
                }

                foreach (int num in allDraws[i])
                {
                    if (counts[num]++ == 0)
                        activeNumbers.Add(num);
                }

                foreach (int num in activeNumbers)
                {
                    if (!frequencyTracker.TryGetValue(num, out var tracker))
                        frequencyTracker[num] = tracker = new FrequencyTracker();

                    double freq = (double)windowSize / counts[num];
                    tracker.Add(freq);
                }
            }
        }

        public class FrequencyTracker
        {
            private readonly Dictionary<double, int> frequencyMap = new();
            private int totalCount = 0;
            private double mostFrequentValue = 0;
            private int maxCount = 0;
            private readonly int precision;

            public FrequencyTracker(int decimalPlaces = 8)
            {
                precision = decimalPlaces;
            }

            public void Add(double value)
            {
                double rounded = Math.Round(value, precision);

                if (!frequencyMap.TryGetValue(rounded, out int count))
                    count = 0;

                frequencyMap[rounded] = ++count;

                if (count > maxCount || (count == maxCount && rounded < mostFrequentValue))
                {
                    mostFrequentValue = rounded;
                    maxCount = count;
                }

                totalCount++;
            }

            public double GetMostFrequent()
            {
                return totalCount == 0 ? 0 : mostFrequentValue;
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            return await _s.Csv22.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}