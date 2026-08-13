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
    public class A312Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A312Model(AppServices s)
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

        public List<SquareDistributionResult> SquareDistributionResults { get; set; } = new();
        public int Rows { get; set; }
        public int Cols { get; set; }
        public int BallsPerDraw { get; set; }

        public class SquareDistributionResult
        {
            public int SquareNumber { get; set; }
            public int EvenCount { get; set; }
            public int OddCount { get; set; }
            public int Count { get; set; }
            public string SquareCells { get; set; } = "";
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            SquareDistributionResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A312_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await AnalyzeSquareDistribution();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await AnalyzeSquareDistribution();
            return Page();
        }

        private async Task AnalyzeSquareDistribution()
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
            (Rows, Cols) = Game?.ToLower() switch
            {
                "keno" => (6, 10), // 60 = 6 * 10
                "blitz" => (4, 5), // 20 = 4 * 5
                "5x36" => (6, 6), // 36 = 6 * 6
                "6x49" => (7, 7), // 49 = 7 * 7
                "1224" => (4, 6), // 24 = 4 * 6
                _ => (6, 10)
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

            var squarePatterns = new Dictionary<int, Dictionary<(int Even, int Odd), int>>();
            var squareCellPositions = new Dictionary<int, string>();

            int totalSquares = 0; // Счётчик всех квадратов

            foreach (var row in draws)
            {
                var matrix = BuildMatrixFromRow(row, numberProps);

                int squareCounter = 1;

                for (int r = 0; r < Rows - 1; r++)
                {
                    for (int c = 0; c < Cols - 1; c++)
                    {
                        int even = 0, odd = 0;
                        int[] values =
                        {
                            matrix[r, c],
                            matrix[r, c + 1],
                            matrix[r + 1, c],
                            matrix[r + 1, c + 1]
                        };

                        foreach (int val in values)
                        {
                            if (val == -1) continue;
                            if (val % 2 == 0) even++;
                            else odd++;
                        }

                        int pos1 = r * Cols + c + 1;
                        int pos2 = r * Cols + c + 2;
                        int pos3 = (r + 1) * Cols + c + 1;
                        int pos4 = (r + 1) * Cols + c + 2;

                        squareCellPositions[squareCounter] = $"{pos1}_{pos2}_{pos3}_{pos4}";

                        if (!squarePatterns.ContainsKey(squareCounter))
                            squarePatterns[squareCounter] = new Dictionary<(int, int), int>();

                        var patternKey = (even, odd);

                        if (!squarePatterns[squareCounter].ContainsKey(patternKey))
                            squarePatterns[squareCounter][patternKey] = 0;

                        squarePatterns[squareCounter][patternKey]++;
                        totalSquares++; // увеличиваем общее количество
                        squareCounter++;
                    }
                }
            }

            SquareDistributionResults.Clear();

            foreach (var square in squarePatterns)
            {
                string pos = squareCellPositions.ContainsKey(square.Key) ? squareCellPositions[square.Key] : "Неизвестно";

                foreach (var pat in square.Value)
                {
                    double chance = totalSquares > 0
                        ? Math.Round((double)pat.Value / totalSquares, 4)
                        : 0.0;

                    SquareDistributionResults.Add(new SquareDistributionResult
                    {
                        SquareNumber = square.Key,
                        EvenCount = pat.Key.Even,
                        OddCount = pat.Key.Odd,
                        Count = pat.Value,
                        SquareCells = pos,
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

            return await _s.Csv312.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}