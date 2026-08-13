using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Reflection;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A65Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A65Model(AppServices s)
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

        [BindProperty]
        public string? SelectedPattern { get; set; }

        public List<PatternMatchResult>? PatternMatches { get; set; }
        public int MaxBallNumber { get; set; }
        public int NumberOfBalls { get; set; }
        public bool UseBonus { get; set; }
        public bool HasTime { get; set; }

        public class PatternMatchResult
        {
            public int StartDraw { get; set; }
            public int StartPosition { get; set; }
            public int EndDraw { get; set; }
            public int EndPosition { get; set; }
            public string Numbers { get; set; } = "";
        }

        public void Dispose()
        {
            PatternMatches?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A65_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await FindPatternMatchesAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await FindPatternMatchesAsync();
            return Page();
        }

        private async Task FindPatternMatchesAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedPattern))
                return;

            // Десериализация паттерна
            List<Cell>? selectedCells;
            try
            {
                selectedCells = System.Text.Json.JsonSerializer.Deserialize<List<Cell>>(SelectedPattern);
            }
            catch
            {
                return;
            }

            if (selectedCells == null || !selectedCells.Any())
                return;

            SetGameParameters(Game!);
            // Получаем тиражи из БД
            var draws = await GetDrawsAsync();
            if (!draws.Any()) return;
            // Формируем карту лотереи: тираж -> [шар -> позиция или '*']
            var lotteryMap = BuildLotteryTable(draws);
            // Нормализуем паттерн
            int minRow = selectedCells.Min(c => c.Row);
            int minCol = selectedCells.Min(c => c.Col);
            var normalizedPattern = selectedCells
                .Select(c => (Row: c.Row - minRow, Col: c.Col - minCol))
                .OrderBy(c => c.Row).ThenBy(c => c.Col)
                .ToList();
            int patternRows = normalizedPattern.Max(p => p.Row) + 1;
            int patternCols = normalizedPattern.Max(p => p.Col) + 1;
            Console.WriteLine("=== Normalized pattern ===");
            foreach (var p in normalizedPattern)
                Console.WriteLine($"({p.Row}, {p.Col})");

            // Поиск совпадений
            PatternMatches = new List<PatternMatchResult>();
            int rowLimit = lotteryMap.Rows.Count - patternRows;
            int colLimit = lotteryMap.Columns.Count - patternCols;

            for (int startRow = 0; startRow <= rowLimit; startRow++)
            {
                for (int startCol = 0; startCol <= colLimit; startCol++)
                {
                    if (IsPatternMatch(lotteryMap, normalizedPattern, startRow, startCol))
                    {
                        string patternString = PatternToString(lotteryMap, normalizedPattern, startRow, startCol);
                        PatternMatches.Add(new PatternMatchResult
                        {
                            StartDraw = startRow + 1,
                            StartPosition = startCol + 1,
                            EndDraw = startRow + patternRows,
                            EndPosition = startCol + patternCols,
                            Numbers = patternString
                        });
                    }
                }
            }
        }

        private void SetGameParameters(string game)
        {
            switch (game.ToLower())
            {
                case "keno": MaxBallNumber = 60; NumberOfBalls = 20; UseBonus = false; HasTime = false; break;
                case "blitz": MaxBallNumber = 20; NumberOfBalls = 8; UseBonus = true; HasTime = true; break;
                case "5x36": MaxBallNumber = 36; NumberOfBalls = 6; UseBonus = false; HasTime = false; break;
                case "6x49": MaxBallNumber = 49; NumberOfBalls = 6; UseBonus = false; HasTime = false; break;
                case "1224": MaxBallNumber = 24; NumberOfBalls = 12; UseBonus = false; HasTime = true; break;
                default: MaxBallNumber = 60; NumberOfBalls = 20; UseBonus = false; HasTime = false; break;
            }
        }
        private async Task<List<object>> GetDrawsAsync()
        {
            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

            query = Direction == "newToOld"
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (DrawCount.HasValue && DrawCount.Value > 0)
                query = query.Take(DrawCount.Value);

            return await query.ToListAsync();
        }
        private DataTable BuildLotteryTable(List<object> draws)
        {
            var table = new DataTable();

            // --- Генерация колонок (как в GenerateUniversalMap) ---
            table.Columns.Add("Тираж", typeof(int));
            table.Columns.Add("Дата", typeof(string));

            if (HasTime)
                table.Columns.Add("Время", typeof(string));

            for (int i = 1; i <= MaxBallNumber; i++)
                table.Columns.Add($"№{i}", typeof(string));

            if (UseBonus)
            {
                for (int i = 1; i <= 4; i++)
                    table.Columns.Add($"Бонус {i}", typeof(string));
            }

            if (!draws.Any())
                return table;

            // --- Оптимизация: считываем PropertyInfo один раз ---
            var first = draws.First();

            var type = first.GetType();

            var propDraw = type.GetProperty("Draw");
            var propDate = type.GetProperty("Date");
            var propTime = HasTime ? type.GetProperty("Time") : null;

            // Находим B1..Bn
            var numberProps = new Dictionary<int, PropertyInfo>();
            for (int i = 1; i <= NumberOfBalls; i++)
            {
                var p = type.GetProperty($"B{i}");
                if (p != null) numberProps[i] = p;
            }

            // Бонус
            var bonusProp = UseBonus ? type.GetProperty("BB") : null;

            // --- Основной цикл ---

            foreach (var rowObj in draws)
            {
                var newRow = table.NewRow();

                // draw, date, time
                newRow["Тираж"] = propDraw?.GetValue(rowObj);
                newRow["Дата"] = propDate?.GetValue(rowObj)?.ToString();
                if (HasTime)
                    newRow["Время"] = propTime?.GetValue(rowObj)?.ToString();

                // заполняем HashSet шаров (как в GenerateUniversalMap)
                HashSet<int> balls = new();

                for (int i = 1; i <= NumberOfBalls; i++)
                {
                    if (!numberProps.ContainsKey(i))
                        continue;

                    var val = numberProps[i].GetValue(rowObj);
                    if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                        balls.Add(num);
                }

                // заполнение карты выпадения
                for (int ball = 1; ball <= MaxBallNumber; ball++)
                {
                    newRow[$"№{ball}"] = balls.Contains(ball) ? ball.ToString() : "*";
                }

                // бонусы
                if (UseBonus)
                {
                    for (int b = 1; b <= 4; b++)
                        newRow[$"Бонус {b}"] = "*";

                    if (bonusProp != null)
                    {
                        var bbVal = bonusProp.GetValue(rowObj);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int bonusBall))
                        {
                            if (bonusBall >= 1 && bonusBall <= 4)
                                newRow[$"Бонус {bonusBall}"] = bonusBall.ToString();
                        }
                    }
                }

                table.Rows.Add(newRow);
            }
            return table;
        }
        private bool IsPatternMatch(DataTable table, List<(int Row, int Col)> pattern, int startRow, int startCol)
        {
            if (pattern == null || pattern.Count == 0) return false;

            // Количество служебных колонок перед "№1": Тираж, Дата, (Время)
            int headerCols = HasTime ? 3 : 2;

            foreach (var (rowOffset, colOffsetPattern) in pattern)
            {
                // строка таблицы (тираж) — просто смещение по вертикали
                int r = startRow + rowOffset;

                // колонка в DataTable = headerCols + startCol(внутри числовой сетки) + смещение внутри паттерна
                int c = headerCols + startCol + colOffsetPattern;

                // границы
                if (r < 0 || r >= table.Rows.Count) return false;
                if (c < 0 || c >= table.Columns.Count) return false;

                string? cell = table.Rows[r][c]?.ToString();
                if (string.IsNullOrWhiteSpace(cell) || cell == "*")
                    return false;
            }
            return true;
        }
        private string PatternToString(DataTable table, List<(int Row, int Col)> pattern, int startRow, int startCol)
        {
            if (pattern == null || pattern.Count == 0) return string.Empty;

            int headerCols = HasTime ? 3 : 2;
            var numbers = new List<string>(pattern.Count);

            foreach (var (rowOffset, colOffsetPattern) in pattern)
            {
                int r = startRow + rowOffset;
                int c = headerCols + startCol + colOffsetPattern;

                if (r < 0 || r >= table.Rows.Count || c < 0 || c >= table.Columns.Count)
                {
                    numbers.Add("*");
                    continue;
                }

                var val = table.Rows[r][c]?.ToString();
                numbers.Add(val ?? "*");
            }

            return string.Join(",", numbers);
        }
        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            if (string.IsNullOrEmpty(SelectedPattern))
                return BadRequest("Паттерн обязателен.");

            List<Cell> selectedPattern;
            try
            {
                selectedPattern = System.Text.Json.JsonSerializer.Deserialize<List<Cell>>(SelectedPattern);
            }
            catch
            {
                return BadRequest("Ошибка разбора паттерна.");
            }

            if (selectedPattern == null || !selectedPattern.Any())
                return BadRequest("Паттерн пуст.");

            return await _s.Csv65.ExportAsync(Game, DrawCount, Direction, selectedPattern, Title);
        }
        public class Cell
        {
            public int Row { get; set; }
            public int Col { get; set; }
        }
    }
}