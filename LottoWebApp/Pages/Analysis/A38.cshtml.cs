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
    public class A38Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A38Model(AppServices s)
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

        public List<MatrixRowResult> MatrixResults { get; set; } = new();
        public int Rows { get; set; }
        public int Cols { get; set; }
        public int BallsPerDraw { get; set; }
        public bool HasTime { get; set; }

        public class MatrixRowResult
        {
            public int Draw { get; set; }
            public string Date { get; set; } = "";
            public string? Time { get; set; }
            public Dictionary<int, string> Cells { get; set; } = new();
        }

        public void Dispose()
        {
            MatrixResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A38_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateMatrixTable();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateMatrixTable();
            return Page();
        }

        private async Task GenerateMatrixTable()
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

            HasTime = Game?.ToLower() == "blitz" || Game?.ToLower() == "1224";

            MatrixResults.Clear();

            foreach (var row in draws)
            {
                // Создаем пустую матрицу
                int[,] matrix = new int[Rows, Cols];
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                        matrix[r, c] = -1;

                // Заполняем номера шаров
                for (int i = 1; i <= BallsPerDraw; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int ball) && ball >= 1 && ball <= Rows * Cols)
                        {
                            int r = (ball - 1) / Cols;
                            int c = (ball - 1) % Cols;
                            matrix[r, c] = ball;
                        }
                    }
                }

                // Формируем строки
                for (int r = 0; r < Rows; r++)
                {
                    var matrixRow = new MatrixRowResult();
                    var drawProp = first.GetType().GetProperty("Draw");
                    var dateProp = first.GetType().GetProperty("Date");
                    var timeProp = HasTime ? first.GetType().GetProperty("Time") : null;

                    if (r == 0)
                    {
                        matrixRow.Draw = Convert.ToInt32(drawProp?.GetValue(row) ?? 0);
                        matrixRow.Date = dateProp?.GetValue(row)?.ToString() ?? "";
                        if (HasTime && timeProp != null)
                        {
                            matrixRow.Time = timeProp.GetValue(row)?.ToString();
                        }
                    }

                    // Заполняем ряд матрицы
                    for (int c = 0; c < Cols; c++)
                    {
                        int val = matrix[r, c];
                        matrixRow.Cells[c + 1] = val == -1 ? "*" : val.ToString();
                    }

                    MatrixResults.Add(matrixRow);
                }

                // Добавляем пустую строку-разделитель
                MatrixResults.Add(new MatrixRowResult());
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv38.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}