using LottoWebApp.Data;
using LottoWebApp.Pages.Analysis;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LottoWebApp.Services
{
    public class A65CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A65CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, List<A65Model.Cell> selectedPattern, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            if (selectedPattern == null || !selectedPattern.Any())
                throw new ArgumentException("Паттерн обязателен.", nameof(selectedPattern));

            var query = new LotteryQueryProvider(_db).GetQuery(game);

            bool isDesc = direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (drawCount.HasValue && drawCount.Value > 0)
                query = query.Take(drawCount.Value);

            var draws = await query.ToListAsync();
            if (!draws.Any())
                throw new InvalidOperationException("Нет данных для экспорта.");

            var first = draws.First();

            // Определяем параметры для игры
            int maxBallNumber, numberOfBalls;
            bool useBonus, hasTime;
            (maxBallNumber, numberOfBalls, useBonus, hasTime) = game?.ToLower() switch
            {
                "keno" => (60, 20, false, false),
                "blitz" => (20, 8, true, true),
                "5x36" => (36, 6, false, false),
                "6x49" => (49, 6, false, false),
                "1224" => (24, 12, false, true),
                _ => (60, 20, false, false)
            };

            // Создаем таблицу лотереи: тираж -> [шар -> позиция или '*']
            var lotteryTable = new DataTable();

            // --- Генерация колонок (как в GenerateUniversalMap) ---
            lotteryTable.Columns.Add("Тираж", typeof(int));
            lotteryTable.Columns.Add("Дата", typeof(string));

            if (hasTime)
                lotteryTable.Columns.Add("Время", typeof(string));

            for (int i = 1; i <= maxBallNumber; i++)
                lotteryTable.Columns.Add($"№{i}", typeof(string));

            if (useBonus)
            {
                for (int i = 1; i <= 4; i++)
                    lotteryTable.Columns.Add($"Бонус {i}", typeof(string));
            }

            if (!draws.Any())
                return null; // или создать пустую таблицу

            // --- Оптимизация: считываем PropertyInfo один раз ---
            var type = first.GetType();

            var propDraw = type.GetProperty("Draw");
            var propDate = type.GetProperty("Date");
            var propTime = hasTime ? type.GetProperty("Time") : null;

            // Находим B1..Bn
            var numberProps = new Dictionary<int, System.Reflection.PropertyInfo>();
            for (int i = 1; i <= numberOfBalls; i++)
            {
                var p = type.GetProperty($"B{i}");
                if (p != null) numberProps[i] = p;
            }

            // Бонус
            var bonusProp = useBonus ? type.GetProperty("BB") : null;

            // --- Основной цикл ---

            foreach (var rowObj in draws)
            {
                var newRow = lotteryTable.NewRow();

                // draw, date, time
                newRow["Тираж"] = propDraw?.GetValue(rowObj);
                newRow["Дата"] = propDate?.GetValue(rowObj)?.ToString();
                if (hasTime)
                    newRow["Время"] = propTime?.GetValue(rowObj)?.ToString();

                // заполняем HashSet шаров (как в GenerateUniversalMap)
                HashSet<int> balls = new();

                for (int i = 1; i <= numberOfBalls; i++)
                {
                    if (!numberProps.ContainsKey(i))
                        continue;

                    var val = numberProps[i].GetValue(rowObj);
                    if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                        balls.Add(num);
                }

                // заполнение карты выпадения
                for (int ball = 1; ball <= maxBallNumber; ball++)
                {
                    newRow[$"№{ball}"] = balls.Contains(ball) ? ball.ToString() : "*";
                }

                // бонусы
                if (useBonus)
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

                lotteryTable.Rows.Add(newRow);
            }

            // Нормализуем паттерн
            int minRow = selectedPattern.Min(c => c.Row);
            int minCol = selectedPattern.Min(c => c.Col);
            var normalizedPattern = selectedPattern
                .Select(c => (Row: c.Row - minRow, Col: c.Col - minCol))
                .OrderBy(c => c.Row).ThenBy(c => c.Col)
                .ToList();

            int patternRows = normalizedPattern.Max(p => p.Row) + 1;
            int patternCols = normalizedPattern.Max(p => p.Col) + 1;

            // Поиск совпадений
            var matches = new List<(int StartDraw, int StartPosition, int EndDraw, int EndPosition, string Numbers)>();

            int rowLimit = lotteryTable.Rows.Count - patternRows;
            int colLimit = lotteryTable.Columns.Count - patternCols;

            // Количество служебных колонок перед "№1": Тираж, Дата, (Время)
            int headerCols = hasTime ? 3 : 2;

            for (int startRow = 0; startRow <= rowLimit; startRow++)
            {
                for (int startCol = 0; startCol <= colLimit - headerCols; startCol++)
                {
                    if (IsPatternMatch(lotteryTable, normalizedPattern, startRow, startCol, headerCols, hasTime))
                    {
                        string patternString = PatternToString(lotteryTable, normalizedPattern, startRow, startCol, headerCols, hasTime);
                        matches.Add((
                            startRow + 1,
                            startCol + 1,
                            startRow + patternRows,
                            startCol + patternCols,
                            patternString
                        ));
                    }
                }
            }

            // Создаем таблицу результатов
            DataTable dt = new DataTable();
            dt.Columns.Add("Начальный тираж", typeof(int));
            dt.Columns.Add("Начальная позиция", typeof(int));
            dt.Columns.Add("Конечный тираж", typeof(int));
            dt.Columns.Add("Конечная позиция", typeof(int));
            dt.Columns.Add("Числа", typeof(string));

            foreach (var match in matches)
            {
                dt.Rows.Add(match.StartDraw, match.StartPosition, match.EndDraw, match.EndPosition, match.Numbers);
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A65_Pattern_Matches" : title;
            string gameStr = game ?? "unknown";
            string drawCountStr = drawCount?.ToString() ?? "all";

            string safeFileName = $"{titlePart}_{gameStr}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
                .Replace(" ", "_")
                .Replace(":", "_")
                .Replace("/", "_")
                .Replace("\\", "_");

            string filePath = await CsvExportService.ExportToCsvAsync(dt, safeFileName);

            string webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            string relativePathWithoutSlash = filePath.TrimStart('/');
            string fullPath = Path.Combine(webRootPath, relativePathWithoutSlash);

            if (!System.IO.File.Exists(fullPath))
                throw new FileNotFoundException("Файл для экспорта не был создан.", fullPath);

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return new FileContentResult(bytes, "text/csv") { FileDownloadName = Path.GetFileName(fullPath) };
        }

        private bool IsPatternMatch(DataTable table, List<(int Row, int Col)> pattern, int startRow, int startCol, int headerCols, bool hasTime)
        {
            if (pattern == null || pattern.Count == 0) return false;

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

        private string PatternToString(DataTable table, List<(int Row, int Col)> pattern, int startRow, int startCol, int headerCols, bool hasTime)
        {
            if (pattern == null || pattern.Count == 0) return string.Empty;

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
    }
}