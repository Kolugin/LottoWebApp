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
    public class A310Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A310Model(AppServices s)
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

        public List<RowDistributionResult> RowDistributionResults { get; set; } = new();
        public int Rows { get; set; }
        public int Cols { get; set; }
        public int BallsPerDraw { get; set; }

        public class RowDistributionResult
        {
            public int Row { get; set; }
            public int Even { get; set; }
            public int Odd { get; set; }
            public int Count { get; set; }
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            RowDistributionResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A310_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await AnalyzeRowDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await AnalyzeRowDistribution();
            return Page();
        }

        private async Task AnalyzeRowDistribution()
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
            int maxBallNumber = Game?.ToLower() switch
            {
                "keno" => 60,
                "blitz" => 20,
                "5x36" => 36,
                "6x49" => 49,
                "1224" => 24,
                _ => 60
            };

            BallsPerDraw = Game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            (Rows, Cols) = Game?.ToLower() switch
            {
                "keno" => (6, 10), // 60 = 6 * 10
                "blitz" => (4, 5), // 20 = 4 * 5
                "5x36" => (6, 6), // 36 = 6 * 6
                "6x49" => (7, 7), // 49 = 7 * 7
                "1224" => (4, 6), // 24 = 4 * 6
                _ => (6, 10)
            };

            var rowDistribution = new Dictionary<int, Dictionary<(int Even, int Odd), int>>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var matrix = BuildMatrixFromRow(row, numberProps);

                for (int r = 0; r < Rows; r++)
                {
                    int even = 0, odd = 0;
                    for (int c = 0; c < Cols; c++)
                    {
                        int val = matrix[r, c];
                        if (val != -1)
                        {
                            if (val % 2 == 0) even++;
                            else odd++;
                        }
                    }

                    var key = (even, odd);

                    if (!rowDistribution.ContainsKey(r + 1))
                        rowDistribution[r + 1] = new Dictionary<(int, int), int>();

                    if (!rowDistribution[r + 1].ContainsKey(key))
                        rowDistribution[r + 1][key] = 0;

                    rowDistribution[r + 1][key]++;
                    totalDraws++;
                }
            }

            RowDistributionResults.Clear();

            foreach (var entry in rowDistribution)
            {
                foreach (var pair in entry.Value)
                {
                    double chance = totalDraws > 0
                        ? Math.Round((double)pair.Value / totalDraws, 4)
                        : 0.0;

                    RowDistributionResults.Add(new RowDistributionResult
                    {
                        Row = entry.Key,
                        Even = pair.Key.Even,
                        Odd = pair.Key.Odd,
                        Count = pair.Value,
                        Chance = chance
                    });
                }
            }
        }

        private int[,] BuildMatrixFromRow(object row, List<System.Reflection.PropertyInfo> numberProps)
        {
            var matrix = new int[Rows, Cols];
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    matrix[r, c] = -1;

            var drawnNumbers = new List<int>();
            for (int i = 1; i <= BallsPerDraw; i++)
            {
                var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                if (prop != null)
                {
                    var val = prop.GetValue(row);
                    if (val != null && int.TryParse(val.ToString(), out int num) && num > 0 && num <= Rows * Cols)
                    {
                        drawnNumbers.Add(num);
                    }
                }
            }

            foreach (var num in drawnNumbers)
            {
                int r = (num - 1) / Cols;
                int c = (num - 1) % Cols;
                matrix[r, c] = num;
            }

            return matrix;
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv310.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}