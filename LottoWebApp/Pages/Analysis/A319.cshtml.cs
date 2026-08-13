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
    public class A319Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A319Model(AppServices s)
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

        public List<TriSqMirResult> TriSqMirResults { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }

        public class TriSqMirResult
        {
            public int TriangularCount { get; set; }
            public int SquareCount { get; set; }
            public int MirroredCount { get; set; }
            public int Frequency { get; set; }
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            TriSqMirResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A319_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateTriSqMirDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateTriSqMirDistribution();
            return Page();
        }

        private async Task GenerateTriSqMirDistribution()
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

            var distribution = new Dictionary<(int Triangular, int Square, int Mirrored), int>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var balls = ExtractBallsFromRow(row, numberProps);
                if (balls.Length == 0) continue;

                var stats = CalculateDrawStats(balls);
                var key = (stats.TriangularCount, stats.SquareCount, stats.MirroredCount);

                if (!distribution.ContainsKey(key))
                    distribution[key] = 0;
                distribution[key]++;
                totalDraws++;
            }

            TriSqMirResults.Clear();

            foreach (var kvp in distribution.OrderBy(x => x.Key))
            {
                int triangular = kvp.Key.Triangular;
                int square = kvp.Key.Square;
                int mirrored = kvp.Key.Mirrored;
                int frequency = kvp.Value;
                double chance = Math.Round(frequency * 100.0 / totalDraws, 4);

                TriSqMirResults.Add(new TriSqMirResult
                {
                    TriangularCount = triangular,
                    SquareCount = square,
                    MirroredCount = mirrored,
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

        private (int TriangularCount, int SquareCount, int MirroredCount) CalculateDrawStats(int[] balls)
        {
            if (balls.Length == 0) return (0, 0, 0);

            int triangularCount = 0;
            int squareCount = 0;
            int mirroredCount = 0;

            foreach (int ball in balls)
            {
                if (ball > 0 && ball <= MaxBallNumber)
                {
                    if (IsTriangularNumber(ball))
                        triangularCount++;
                    if (IsPerfectSquare(ball))
                        squareCount++;
                    if (IsMirroredNumber(ball))
                        mirroredCount++;
                }
            }

            return (triangularCount, squareCount, mirroredCount);
        }

        private bool IsTriangularNumber(int number)
        {
            if (number < 1) return false;
            // Формула: n(n+1)/2 = number => n^2 + n - 2*number = 0
            double n = (-1 + Math.Sqrt(1 + 8 * number)) / 2;
            return n == Math.Floor(n);
        }

        private bool IsPerfectSquare(int number)
        {
            if (number < 1) return false;
            int sqrt = (int)Math.Sqrt(number);
            return sqrt * sqrt == number;
        }

        private bool IsMirroredNumber(int number)
        {
            if (number < 10) return false; // Однозначные числа не считаются зеркальными
            string str = number.ToString();
            char[] chars = str.ToCharArray();
            Array.Reverse(chars);
            string reversed = new string(chars);
            return str == reversed;
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv319.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}