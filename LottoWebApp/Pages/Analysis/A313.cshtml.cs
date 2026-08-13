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
    public class A313Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A313Model(AppServices s)
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

        public List<SumDistributionResult> SumDistributionResults { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }

        public class SumDistributionResult
        {
            public int SumHit { get; set; }
            public int SumMissed { get; set; }
            public int Frequency { get; set; }
            public double Probability { get; set; }
        }

        public void Dispose()
        {
            SumDistributionResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A313_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateSumDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateSumDistribution();
            return Page();
        }

        private async Task GenerateSumDistribution()
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

            var distribution = new Dictionary<int, (int Frequency, int SumMissedTotal)>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var balls = ExtractBallsFromRow(row, numberProps);
                if (balls.Length == 0) continue;

                totalDraws++;

                int sum = balls.Sum();

                // Вычисляем сумму невыпавших
                var allBalls = Enumerable.Range(1, MaxBallNumber);
                var unhitBalls = allBalls.Except(balls);
                int sumMissed = unhitBalls.Sum();

                if (!distribution.TryGetValue(sum, out var value))
                    value = (0, 0);

                distribution[sum] = (value.Frequency + 1, value.SumMissedTotal + sumMissed);
            }

            SumDistributionResults.Clear();

            foreach (var kvp in distribution.OrderBy(x => x.Key))
            {
                int sum = kvp.Key;
                int frequency = kvp.Value.Frequency;
                int avgMissedSum = kvp.Value.SumMissedTotal / frequency;
                double probability = Math.Round(frequency * 100.0 / totalDraws, 4);

                SumDistributionResults.Add(new SumDistributionResult
                {
                    SumHit = sum,
                    SumMissed = avgMissedSum,
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

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv313.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}