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
    public class A53Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A53Model(AppServices s)
        {
            _s = s;
        }

        [BindProperty(SupportsGet = true)]
        public string? Game { get; set; }

        [BindProperty(SupportsGet = true)]
        public int SelectedBall { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int? DrawCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Direction { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        public Dictionary<int, Dictionary<int, int[,]>>? TransitionCountByNumber { get; set; }
        public int NumberOfBalls { get; set; }
        public int MaxColumn { get; set; }

        public void Dispose()
        {
            TransitionCountByNumber?.Clear();
        }

        public async Task OnGetAsync()
        {
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await CalculateAndExportTransitionByYtoZ();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await CalculateAndExportTransitionByYtoZ();
            return Page();
        }

        private async Task CalculateAndExportTransitionByYtoZ()
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
            (NumberOfBalls, MaxColumn) = Game?.ToLower() switch
            {
                "keno" => (60, 20),
                "blitz" => (20, 8),
                "5x36" => (36, 6),
                "6x49" => (49, 6),
                "1224" => (24, 12),
                _ => (60, 20)
            };

            int maxIntervals = 10;
            TransitionCountByNumber = new Dictionary<int, Dictionary<int, int[,]>>();
            for (int y = 1; y <= NumberOfBalls; y++)
            {
                TransitionCountByNumber[y] = new Dictionary<int, int[,]>();
                for (int z = 1; z <= NumberOfBalls; z++)
                    TransitionCountByNumber[y][z] = new int[maxIntervals, MaxColumn];
            }

            var currentDrawBalls = new List<int>();
            var futureDrawBalls = new List<int>();

            for (int i = 0; i < draws.Count - maxIntervals; i++)
            {
                currentDrawBalls.Clear();
                for (int j = 1; j <= MaxColumn; j++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(draws[i]);
                        if (val != null && int.TryParse(val.ToString(), out int num))
                        {
                            currentDrawBalls.Add(num);
                        }
                    }
                }

                for (int interval = 1; interval <= maxIntervals; interval++)
                {
                    if (i + interval >= draws.Count) break;

                    futureDrawBalls.Clear();
                    for (int k = 1; k <= MaxColumn; k++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{k}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(draws[i + interval]);
                            if (val != null && int.TryParse(val.ToString(), out int num))
                            {
                                futureDrawBalls.Add(num);
                            }
                        }
                    }

                    for (int pos = 0; pos < currentDrawBalls.Count; pos++)
                    {
                        int currentBall = currentDrawBalls[pos];
                        for (int futurePos = 0; futurePos < futureDrawBalls.Count; futurePos++)
                        {
                            int futureBall = futureDrawBalls[futurePos];
                            if (futureBall > 0 && futureBall <= NumberOfBalls && currentBall > 0 && currentBall <= NumberOfBalls)
                                TransitionCountByNumber[currentBall][futureBall][interval - 1, futurePos]++;
                        }
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv53.ExportAsync(Game, DrawCount, Direction, Title, SelectedBall);
        }
    }
}