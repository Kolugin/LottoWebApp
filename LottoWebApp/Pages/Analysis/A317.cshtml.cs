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
    public class A317Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A317Model(AppServices s)
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

        public List<PrimeCompositeResult> PrimeCompositeResults { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }

        public class PrimeCompositeResult
        {
            public int PrimeCount { get; set; }
            public int CompositeCount { get; set; }
            public int Frequency { get; set; }
            public double Probability { get; set; }
        }

        public void Dispose()
        {
            PrimeCompositeResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A317_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GeneratePrimeCompositeDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GeneratePrimeCompositeDistribution();
            return Page();
        }

        private async Task GeneratePrimeCompositeDistribution()
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

            var distribution = new Dictionary<(int Prime, int Composite), int>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var balls = ExtractBallsFromRow(row, numberProps);
                if (balls.Length == 0) continue;

                var stats = CalculateDrawStats(balls);
                var key = (stats.PrimeCount, stats.CompositeCount);

                if (!distribution.ContainsKey(key))
                    distribution[key] = 0;
                distribution[key]++;
                totalDraws++;
            }

            PrimeCompositeResults.Clear();

            var sortedDistribution = distribution
                .OrderBy(kvp => kvp.Key.Prime)
                .ThenBy(kvp => kvp.Key.Composite);

            foreach (var kvp in sortedDistribution)
            {
                int prime = kvp.Key.Prime;
                int composite = kvp.Key.Composite;
                int frequency = kvp.Value;
                double probability = Math.Round(frequency * 100.0 / totalDraws, 4);

                PrimeCompositeResults.Add(new PrimeCompositeResult
                {
                    PrimeCount = prime,
                    CompositeCount = composite,
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

        private (int PrimeCount, int CompositeCount) CalculateDrawStats(int[] balls)
        {
            if (balls.Length == 0) return (0, 0);

            int primeCount = 0;
            int compositeCount = 0;

            foreach (int ball in balls)
            {
                if (IsPrime(ball))
                    primeCount++;
                else if (ball > 1) // 1 не является ни простым, ни составным
                    compositeCount++;
            }

            return (primeCount, compositeCount);
        }

        private bool IsPrime(int number)
        {
            if (number < 2) return false;
            if (number == 2) return true;
            if (number % 2 == 0) return false;

            for (int i = 3; i * i <= number; i += 2)
            {
                if (number % i == 0)
                    return false;
            }

            return true;
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv317.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}