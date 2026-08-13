using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Text;

namespace LottoWebApp.Services
{
    public class A63CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A63CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, int minHorizontalLineLength, int minVerticalLineLength, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            if (minHorizontalLineLength < 2) minHorizontalLineLength = 2;
            if (minVerticalLineLength < 2) minVerticalLineLength = 2;

            var query = new LotteryQueryProvider(_db).GetQuery(game);

            bool isDesc = direction == "newToOld";
            query = isDesc
                ? query.OrderBy(e => EF.Property<int>(e, "Draw")) // Инвертируем: сначала новые = по возрастанию
                : query.OrderByDescending(e => EF.Property<int>(e, "Draw")); // сначала старые = по убыванию

            if (drawCount.HasValue && drawCount.Value > 0)
                query = query.Take(drawCount.Value);

            var draws = await query.ToListAsync();
            if (!draws.Any())
                throw new InvalidOperationException("Нет данных для экспорта.");

            var first = draws.First();

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any())
                throw new InvalidOperationException("Не найдены столбцы чисел.");

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

            // Создаем карту лотереи: тираж -> [шар -> позиция или '*']
            var lotteryMap = new List<Dictionary<int, string>>();
            var drawProp = first.GetType().GetProperty("Draw");
            var dateProp = first.GetType().GetProperty("Date");
            var timeProp = hasTime ? first.GetType().GetProperty("Time") : null;

            foreach (var row in draws)
            {
                var rowMap = new Dictionary<int, string>();

                // Добавляем служебные поля
                if (drawProp != null)
                {
                    rowMap[-1] = drawProp.GetValue(row)?.ToString() ?? ""; // Для номера тиража
                }
                if (dateProp != null)
                {
                    rowMap[-2] = dateProp.GetValue(row)?.ToString() ?? ""; // Для даты
                }
                if (hasTime && timeProp != null)
                {
                    rowMap[-3] = timeProp.GetValue(row)?.ToString() ?? ""; // Для времени
                }

                // Заполняем числа по шарам (№1, №2, ..., №N)
                for (int ball = 1; ball <= maxBallNumber; ball++)
                {
                    string value = "*";
                    for (int i = 1; i <= numberOfBalls; i++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(row);
                            if (val != null && int.TryParse(val.ToString(), out int num) && num == ball)
                            {
                                value = ball.ToString();
                                break;
                            }
                        }
                    }
                    rowMap[ball] = value;
                }

                // Если есть бонус
                if (useBonus)
                {
                    for (int bonusIndex = 1; bonusIndex <= 4; bonusIndex++)
                    {
                        rowMap[-10 - bonusIndex] = "*"; // Используем отрицательные ключи для бонусов
                    }
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var bbVal = bbProp.GetValue(row);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int bonusBall) && bonusBall >= 1 && bonusBall <= 4)
                        {
                            rowMap[-10 - bonusBall] = bonusBall.ToString();
                        }
                    }
                }

                lotteryMap.Add(rowMap);
            }

            // Создаем DataTable
            DataTable dt = new DataTable();
            dt.Columns.Add("Начальная позиция", typeof(int));
            dt.Columns.Add("Конечная позиция", typeof(int));
            dt.Columns.Add("Начальный тираж", typeof(int));
            dt.Columns.Add("Конечный тираж", typeof(int));
            dt.Columns.Add("Тип линии", typeof(string));
            dt.Columns.Add("Числа", typeof(string));

            int rowCount = lotteryMap.Count;

            // Поиск горизонтальных линий
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                int start = -1;
                string currentLineNumbers = "";

                for (int col = 1; col <= maxBallNumber; col++)
                {
                    string cellValue = lotteryMap[rowIndex][col];

                    if (cellValue != "*")
                    {
                        if (start == -1)
                        {
                            start = col;
                        }
                        currentLineNumbers += (currentLineNumbers == "" ? "" : ", ") + cellValue;
                    }
                    else
                    {
                        int count = currentLineNumbers.Split(',').Length;
                        if (count >= minHorizontalLineLength)
                        {
                            dt.Rows.Add(start, start + count - 1, rowIndex + 1, rowIndex + 1, "Горизонтальная", currentLineNumbers);
                        }
                        start = -1;
                        currentLineNumbers = "";
                    }
                }

                int finalCount = currentLineNumbers.Split(',').Length;
                if (finalCount >= minHorizontalLineLength)
                {
                    dt.Rows.Add(start, start + finalCount - 1, rowIndex + 1, rowIndex + 1, "Горизонтальная", currentLineNumbers);
                }
            }

            // Поиск вертикальных линий
            for (int colIndex = 1; colIndex <= maxBallNumber; colIndex++)
            {
                int start = -1;
                string currentLineNumbers = "";

                for (int row = 0; row < lotteryMap.Count; row++)
                {
                    string cellValue = lotteryMap[row][colIndex];

                    if (cellValue != "*")
                    {
                        if (start == -1)
                        {
                            start = row + 1;
                        }
                        currentLineNumbers += (currentLineNumbers == "" ? "" : ", ") + cellValue;
                    }
                    else
                    {
                        int count = currentLineNumbers.Split(',').Length;
                        if (count >= minVerticalLineLength)
                        {
                            dt.Rows.Add(colIndex, colIndex, start, start + count - 1, "Вертикальная", currentLineNumbers);
                        }
                        start = -1;
                        currentLineNumbers = "";
                    }
                }

                int finalCount = currentLineNumbers.Split(',').Length;
                if (finalCount >= minVerticalLineLength)
                {
                    dt.Rows.Add(colIndex, colIndex, start, start + finalCount - 1, "Вертикальная", currentLineNumbers);
                }
            }

            // Поиск пересечений (фигур)
            var horizontalLines = new List<(int StartCol, int EndCol, int Row, List<string> Numbers)>();
            var verticalLines = new List<(int Col, int StartRow, int EndRow, List<string> Numbers)>();

            foreach (DataRow dr in dt.Rows)
            {
                string lineType = dr["Тип линии"].ToString();
                if (lineType == "Горизонтальная")
                {
                    horizontalLines.Add((
                        Convert.ToInt32(dr["Начальная позиция"]),
                        Convert.ToInt32(dr["Конечная позиция"]),
                        Convert.ToInt32(dr["Начальный тираж"]) - 1, // индекс строки
                        dr["Числа"].ToString().Split(',').Select(n => n.Trim()).ToList()
                    ));
                }
                else if (lineType == "Вертикальная")
                {
                    verticalLines.Add((
                        Convert.ToInt32(dr["Начальная позиция"]) - 1, // индекс столбца
                        Convert.ToInt32(dr["Начальный тираж"]) - 1, // индекс строки
                        Convert.ToInt32(dr["Конечный тираж"]) - 1, // индекс строки
                        dr["Числа"].ToString().Split(',').Select(n => n.Trim()).ToList()
                    ));
                }
            }

            foreach (var h in horizontalLines)
            {
                foreach (var v in verticalLines)
                {
                    // Пересечение по координатам
                    if (h.Row >= v.StartRow && h.Row <= v.EndRow &&
                        v.Col >= h.StartCol - 1 && v.Col <= h.EndCol - 1) // -1 потому что в карте ключи начинаются с 1
                    {
                        string intersectValue = lotteryMap[h.Row][v.Col + 1]; // +1 потому что в карте ключи начинаются с 1
                        if (string.IsNullOrEmpty(intersectValue) || intersectValue == "*") continue;

                        if (h.Numbers.Contains(intersectValue) && v.Numbers.Contains(intersectValue))
                        {
                            string shapeInfo = $"Пересекающееся число: {intersectValue} | Горизонталь: ({string.Join(", ", h.Numbers)}) | Вертикаль: ({string.Join(", ", v.Numbers)})";

                            dt.Rows.Add(h.StartCol, h.EndCol, h.Row + 1, h.Row + 1, "Пересечение", shapeInfo);
                        }
                    }
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A63_Horizontal_Vertical_Lines_And_Shapes" : title;
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
    }
}