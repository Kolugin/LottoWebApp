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
    public class A314Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A314Model(AppServices s)
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

        public List<AvgMedDistributionResult> AvgMedDistributionResults { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }

        public class AvgMedDistributionResult
        {
            public double Average { get; set; }
            public double Median { get; set; }
            public int Frequency { get; set; }
            public double Probability { get; set; }
        }

        public void Dispose()
        {
            AvgMedDistributionResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A314_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateAverageMedianDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateAverageMedianDistribution();
            return Page();
        }

        private async Task GenerateAverageMedianDistribution()
        {
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

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any()) return;

            // Определяем параметры для игры
            (NumberOfBalls, MaxBallNumber) = Game?.ToLower() switch
            {
                "keno" => (20, 60),
                "blitz" => (8, 20),
                "5x36" => (6, 36),
                "6x49" => (6, 49),
                "1224" => (12, 24),
                _ => (20, 60)
            };

            var distribution = new Dictionary<(double Average, double Median), int>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var balls = ExtractBallsFromRow(row, numberProps);
                if (balls.Length == 0) continue;

                totalDraws++;

                var stats = CalculateDrawStats(balls);
                var avg = Math.Round(stats.Average, 2);
                var median = Math.Round(stats.Median, 2);

                var key = (avg, median);
                if (!distribution.ContainsKey(key))
                    distribution[key] = 0;
                distribution[key]++;
            }

            AvgMedDistributionResults.Clear();

            foreach (var kvp in distribution.OrderBy(x => x.Key))
            {
                double average = kvp.Key.Average;
                double median = kvp.Key.Median;
                int frequency = kvp.Value;
                double probability = Math.Round(frequency * 100.0 / totalDraws, 4);

                AvgMedDistributionResults.Add(new AvgMedDistributionResult
                {
                    Average = average,
                    Median = median,
                    Frequency = frequency,
                    Probability = probability
                });
            }
        }

        private int[] ExtractBallsFromRow(object row, List<System.Reflection.PropertyInfo> numberProps)
        {
            var balls = new List<int>();
            for (int i = 1; i <= NumberOfBalls; i++)
            {
                var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                if (prop != null)
                {
                    var val = prop.GetValue(row);
                    if (val != null && int.TryParse(val.ToString(), out int ball) && ball != 0)
                    {
                        balls.Add(ball);
                    }
                }
            }
            return balls.ToArray();
        }

        private (double Average, double Median) CalculateDrawStats(int[] balls)
        {
            if (balls.Length == 0) return (0, 0);

            double average = balls.Average();

            var sortedBalls = balls.OrderBy(x => x).ToArray();
            int len = sortedBalls.Length;
            double median = len % 2 == 0
                ? (sortedBalls[len / 2 - 1] + sortedBalls[len / 2]) / 2.0
                : sortedBalls[len / 2];

            return (average, median);
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv314.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}