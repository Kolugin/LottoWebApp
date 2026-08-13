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
    public class A315Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A315Model(AppServices s)
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

        public List<VarianceStdDevResult> VarianceStdDevResults { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }

        public class VarianceStdDevResult
        {
            public double Variance { get; set; }
            public double StdDev { get; set; }
            public int Frequency { get; set; }
            public double Probability { get; set; }
        }

        public void Dispose()
        {
            VarianceStdDevResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A315_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateVarianceStdDevDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateVarianceStdDevDistribution();
            return Page();
        }

        private async Task GenerateVarianceStdDevDistribution()
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

            var distribution = new Dictionary<(double Variance, double StdDev), int>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var balls = ExtractBallsFromRow(row, numberProps);
                if (balls.Length == 0) continue;

                var stats = CalculateDrawStats(balls);
                var variance = Math.Round(stats.Variance, 4);
                var stdDev = Math.Round(stats.StdDev, 4);

                var key = (variance, stdDev);
                if (!distribution.ContainsKey(key))
                    distribution[key] = 0;
                distribution[key]++;
                totalDraws++;
            }

            VarianceStdDevResults.Clear();

            foreach (var kvp in distribution.OrderBy(x => x.Key))
            {
                double variance = kvp.Key.Variance;
                double stdDev = kvp.Key.StdDev;
                int frequency = kvp.Value;
                double probability = Math.Round(frequency * 100.0 / totalDraws, 4);

                VarianceStdDevResults.Add(new VarianceStdDevResult
                {
                    Variance = variance,
                    StdDev = stdDev,
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

        private (double Average, double Variance, double StdDev) CalculateDrawStats(int[] balls)
        {
            if (balls.Length == 0) return (0, 0, 0);

            double average = balls.Average();
            double variance = balls.Sum(x => Math.Pow(x - average, 2)) / balls.Length;
            double stdDev = Math.Sqrt(variance);

            return (average, variance, stdDev);
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv315.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}