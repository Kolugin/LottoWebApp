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
    public class A39Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A39Model(AppServices s)
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
            public bool IsHeader { get; set; }
            public bool IsReset { get; set; }
        }

        public void Dispose()
        {
            MatrixResults.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A39_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateMatrixWithOccurrences();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateMatrixWithOccurrences();
            return Page();
        }

        private async Task GenerateMatrixWithOccurrences()
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

            int[,] totalOccurrences = new int[Rows, Cols];

            void ResetMatrix()
            {
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                        totalOccurrences[r, c] = -1;
            }

            ResetMatrix();

            int shift = HasTime ? 3 : 2;

            string? lastDraw = null;
            string? lastDate = null;
            string? lastTime = null;

            MatrixResults.Clear();

            foreach (var row in draws)
            {
                var drawProp = first.GetType().GetProperty("Draw");
                var dateProp = first.GetType().GetProperty("Date");
                var timeProp = HasTime ? first.GetType().GetProperty("Time") : null;

                string? draw = drawProp?.GetValue(row)?.ToString();
                string? date = dateProp?.GetValue(row)?.ToString();
                string? time = HasTime && timeProp != null ? timeProp.GetValue(row)?.ToString() : null;

                // Если изменился заголовок — создаём строку-заголовок
                if (draw != lastDraw || date != lastDate || time != lastTime)
                {
                    var headerRow = new MatrixRowResult
                    {
                        Draw = Convert.ToInt32(draw),
                        Date = date ?? "",
                        Time = time,
                        IsHeader = true
                    };
                    MatrixResults.Add(headerRow);

                    lastDraw = draw;
                    lastDate = date;
                    lastTime = time;
                }

                // Обработка шаров
                for (int i = 1; i <= BallsPerDraw; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int ball) && ball > 0 && ball <= Rows * Cols)
                        {
                            int r = (ball - 1) / Cols;
                            int c = (ball - 1) % Cols;

                            if (totalOccurrences[r, c] == -1)
                                totalOccurrences[r, c] = 1;
                            else
                                totalOccurrences[r, c]++;
                        }
                    }
                }

                // Добавляем строки матрицы
                for (int r = 0; r < Rows; r++)
                {
                    var matrixRow = new MatrixRowResult
                    {
                        Draw = 0, // Пустое значение для строк матрицы
                        Date = "",
                        Time = "",
                        IsHeader = false
                    };

                    for (int c = 0; c < Cols; c++)
                    {
                        if (totalOccurrences[r, c] == -1)
                            matrixRow.Cells[c + 1] = "*";
                        else
                            matrixRow.Cells[c + 1] = totalOccurrences[r, c].ToString();
                    }

                    MatrixResults.Add(matrixRow);
                }

                // Проверка на полную матрицу
                bool isMatrixFull = true;
                for (int r = 0; r < Rows && isMatrixFull; r++)
                    for (int c = 0; c < Cols && isMatrixFull; c++)
                        if (totalOccurrences[r, c] == -1)
                            isMatrixFull = false;

                if (isMatrixFull)
                {
                    var resetRow = new MatrixRowResult
                    {
                        IsReset = true
                    };
                    resetRow.Cells[0] = "Сброс матрицы";
                    MatrixResults.Add(resetRow);

                    // Пустая строка-разделитель
                    MatrixResults.Add(new MatrixRowResult());

                    ResetMatrix();

                    continue; 
                }
                for (int r = 0; r < Rows; r++)
                {
                    var matrixRow = new MatrixRowResult { Draw = 0, Date = "", Time = "" };
                    for (int c = 0; c < Cols; c++)
                        matrixRow.Cells[c + 1] = totalOccurrences[r, c] == -1 ? "*" : totalOccurrences[r, c].ToString();
                    MatrixResults.Add(matrixRow);
                }

            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv39.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}