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
    public class A311Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A311Model(AppServices s)
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

        public List<ColumnDistributionResult> ColumnDistributionResults { get; set; } = new();
        public int Rows { get; set; }
        public int Cols { get; set; }
        public int BallsPerDraw { get; set; }

        public class ColumnDistributionResult
        {
            public int ColumnNumber { get; set; }
            public string NumbersCombination { get; set; } = "";
            public int Count { get; set; }
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            ColumnDistributionResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A311_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await AnalyzeColumnDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await AnalyzeColumnDistribution();
            return Page();
        }

        private async Task AnalyzeColumnDistribution()
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

            var columnDistribution = new Dictionary<int, Dictionary<string, int>>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var matrix = BuildMatrixFromRow(row, numberProps);

                for (int c = 0; c < Cols; c++)
                {
                    if (!columnDistribution.ContainsKey(c + 1))
                        columnDistribution[c + 1] = new Dictionary<string, int>();

                    var columnNumbers = new List<int>();
                    for (int r = 0; r < Rows; r++)
                    {
                        if (matrix[r, c] != -1)
                            columnNumbers.Add(matrix[r, c]);
                    }

                    string key = columnNumbers.Count == 0
                        ? "Отсутствует"
                        : string.Join("_", columnNumbers.OrderBy(x => x));

                    if (!columnDistribution[c + 1].ContainsKey(key))
                        columnDistribution[c + 1][key] = 0;

                    columnDistribution[c + 1][key]++;
                    totalDraws++;
                }
            }

            ColumnDistributionResults.Clear();

            foreach (var col in columnDistribution)
            {
                foreach (var pair in col.Value)
                {
                    double chance = totalDraws > 0
                        ? Math.Round((double)pair.Value / totalDraws, 4)
                        : 0.0;

                    ColumnDistributionResults.Add(new ColumnDistributionResult
                    {
                        ColumnNumber = col.Key,
                        NumbersCombination = pair.Key,
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

            return await _s.Csv311.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}