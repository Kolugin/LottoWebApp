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
    public class A318Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A318Model(AppServices s)
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

        public List<LowHighResult> LowHighResults { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }

        public class LowHighResult
        {
            public int LowCount { get; set; }
            public int HighCount { get; set; }
            public int Frequency { get; set; }
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            LowHighResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A318_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateLowHighDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateLowHighDistribution();
            return Page();
        }

        private async Task GenerateLowHighDistribution()
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

            var distribution = new Dictionary<(int Low, int High), int>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var balls = ExtractBallsFromRow(row, numberProps);
                if (balls.Length == 0) continue;

                var stats = CalculateDrawStats(balls);
                var key = (stats.LowCount, stats.HighCount);

                if (!distribution.ContainsKey(key))
                    distribution[key] = 0;
                distribution[key]++;
                totalDraws++;
            }

            LowHighResults.Clear();

            foreach (var kvp in distribution.OrderBy(x => x.Key))
            {
                int low = kvp.Key.Low;
                int high = kvp.Key.High;
                int frequency = kvp.Value;
                double chance = Math.Round(frequency * 100.0 / totalDraws, 4);

                LowHighResults.Add(new LowHighResult
                {
                    LowCount = low,
                    HighCount = high,
                    Frequency = frequency,
                    Chance = chance
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

        private (int LowCount, int HighCount) CalculateDrawStats(int[] balls)
        {
            if (balls.Length == 0) return (0, 0);

            int lowCount = 0;
            int highCount = 0;

            int threshold = MaxBallNumber / 2;

            foreach (int ball in balls)
            {
                if (ball > 0 && ball <= MaxBallNumber)
                {
                    if (ball <= threshold)
                        lowCount++;
                    else
                        highCount++;
                }
            }

            return (lowCount, highCount);
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv318.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}