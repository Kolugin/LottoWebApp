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
    public class A320Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A320Model(AppServices s)
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

        public List<PauseDistributionResult> PauseDistributionResults { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }
        public bool HasBonus { get; set; }

        public class PauseDistributionResult
        {
            public string Ball { get; set; } = "";
            public int PauseCount { get; set; }
            public int Occurrences { get; set; }
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            PauseDistributionResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A320_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateBallAfterPauseStats();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateBallAfterPauseStats();
            return Page();
        }

        private async Task GenerateBallAfterPauseStats()
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
            (MaxBallNumber, NumberOfBalls, HasBonus) = Game?.ToLower() switch
            {
                "keno" => (60, 20, false),
                "blitz" => (20, 8, true),
                "5x36" => (36, 6, false),
                "6x49" => (49, 6, false),
                "1224" => (24, 12, false),
                _ => (60, 20, false)
            };

            int maxPause = 30;
            int[,] pauseFrequencies = new int[MaxBallNumber + 1, maxPause + 1];
            int[,] pauseOccurrences = new int[MaxBallNumber + 1, maxPause + 1];
            int[] currentPauses = new int[MaxBallNumber + 1];

            int bonusCount = 4;
            int[,] bonusFrequencies = new int[bonusCount + 1, maxPause + 1];
            int[,] bonusOccurrences = new int[bonusCount + 1, maxPause + 1];
            int[] bonusPauses = new int[bonusCount + 1];

            foreach (var row in draws)
            {
                var drawn = new HashSet<int>();
                for (int i = 1; i <= NumberOfBalls; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                        {
                            drawn.Add(num);
                        }
                    }
                }

                // Основные шары
                for (int ball = 1; ball <= MaxBallNumber; ball++)
                {
                    int pause = currentPauses[ball];
                    int clipped = Math.Min(pause, maxPause);
                    pauseOccurrences[ball, clipped]++;

                    if (drawn.Contains(ball))
                    {
                        pauseFrequencies[ball, clipped]++;
                        currentPauses[ball] = 0;
                    }
                    else
                    {
                        currentPauses[ball]++;
                    }
                }

                // Бонусные шары
                if (HasBonus)
                {
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var bbVal = bbProp.GetValue(row);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int bonusBall))
                        {
                            for (int b = 1; b <= bonusCount; b++)
                            {
                                int pause = bonusPauses[b];
                                int clipped = Math.Min(pause, maxPause);
                                bonusOccurrences[b, clipped]++;

                                if (bonusBall == b)
                                {
                                    bonusFrequencies[b, clipped]++;
                                    bonusPauses[b] = 0;
                                }
                                else
                                {
                                    bonusPauses[b]++;
                                }
                            }
                        }
                    }
                }
            }

            PauseDistributionResults.Clear();

            // Основная таблица
            for (int ball = 1; ball <= MaxBallNumber; ball++)
            {
                for (int pause = 0; pause <= maxPause; pause++)
                {
                    int hits = pauseFrequencies[ball, pause];
                    int total = pauseOccurrences[ball, pause];
                    if (total == 0 || hits == 0) continue;

                    double chance = Math.Round((double)hits / total, 4);

                    PauseDistributionResults.Add(new PauseDistributionResult
                    {
                        Ball = $"№{ball}",
                        PauseCount = pause,
                        Occurrences = hits,
                        Chance = chance
                    });
                }
            }

            // Бонусные шары
            if (HasBonus)
            {
                for (int b = 1; b <= bonusCount; b++)
                {
                    for (int pause = 0; pause <= maxPause; pause++)
                    {
                        int hits = bonusFrequencies[b, pause];
                        int total = bonusOccurrences[b, pause];
                        if (total == 0 || hits == 0) continue;

                        double chance = Math.Round((double)hits / total, 4);

                        PauseDistributionResults.Add(new PauseDistributionResult
                        {
                            Ball = $"Бонус {b}",
                            PauseCount = pause,
                            Occurrences = hits,
                            Chance = chance
                        });
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv320.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}