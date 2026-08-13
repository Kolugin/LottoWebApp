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
    public class A36Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A36Model(AppServices s)
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

        public List<DistributionResult>? DistributionResults { get; set; }
        public int NumberOfBalls { get; set; }

        public class DistributionResult
        {
            public int EvenCount { get; set; }
            public int OddCount { get; set; }
            public int TotalCount { get; set; }
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            DistributionResults?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A36_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateEvenOddDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateEvenOddDistribution();
            return Page();
        }

        private async Task GenerateEvenOddDistribution()
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

            // Определяем количество шаров для игры
            NumberOfBalls = Game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            var evenOddDistribution = new Dictionary<int, int>();
            for (int i = 0; i <= NumberOfBalls; i++)
            {
                evenOddDistribution[i] = 0;
            }

            int totalDraws = 0;

            foreach (var row in draws)
            {
                int evenCount = 0;

                for (int j = 1; j <= NumberOfBalls; j++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int currentBall) && currentBall != 0 && currentBall % 2 == 0)
                        {
                            evenCount++;
                        }
                    }
                }

                int oddCount = NumberOfBalls - evenCount;
                if (evenCount + oddCount > 0)
                {
                    evenOddDistribution[evenCount]++;
                    totalDraws++;
                }
            }

            DistributionResults = new List<DistributionResult>();

            foreach (var entry in evenOddDistribution)
            {
                int evenNumbers = entry.Key;
                int oddNumbers = NumberOfBalls - evenNumbers;
                int count = entry.Value;

                double chance = totalDraws > 0 ? Math.Round((double)count / totalDraws * 100, 4) : 0.0;

                DistributionResults.Add(new DistributionResult
                {
                    EvenCount = evenNumbers,
                    OddCount = oddNumbers,
                    TotalCount = count,
                    Chance = chance
                });
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv36.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}