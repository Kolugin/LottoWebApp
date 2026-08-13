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
    public class A64CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A64CsvExportHelper(LottoDbContext db)
        {
            _db = db;   
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, int minHorizontalLineLength, int minVerticalLineLength, int minDiagonalLineLength, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            if (minHorizontalLineLength < 2) minHorizontalLineLength = 2;
            if (minVerticalLineLength < 2) minVerticalLineLength = 2;
            if (minDiagonalLineLength < 2) minDiagonalLineLength = 2;

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
                        if (currentLineNumbers.Split(',').Length >= minHorizontalLineLength)
                        {
                            dt.Rows.Add(start, start + currentLineNumbers.Split(',').Length - 1, rowIndex + 1, rowIndex + 1, "Горизонтальная", currentLineNumbers);
                        }
                        start = -1;
                        currentLineNumbers = "";
                    }
                }

                if (currentLineNumbers.Split(',').Length >= minHorizontalLineLength)
                {
                    dt.Rows.Add(start, start + currentLineNumbers.Split(',').Length - 1, rowIndex + 1, rowIndex + 1, "Горизонтальная", currentLineNumbers);
                }
            }

            // Поиск вертикальных линий
            for (int ballIndex = 1; ballIndex <= maxBallNumber; ballIndex++)
            {
                int start = -1;
                string currentLineNumbers = "";

                for (int row = 0; row < lotteryMap.Count; row++)
                {
                    string cellValue = lotteryMap[row][ballIndex];

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
                        if (currentLineNumbers.Split(',').Length >= minVerticalLineLength)
                        {
                            dt.Rows.Add(ballIndex, ballIndex, start, start + currentLineNumbers.Split(',').Length - 1, "Вертикальная", currentLineNumbers);
                        }
                        start = -1;
                        currentLineNumbers = "";
                    }
                }

                if (currentLineNumbers.Split(',').Length >= minVerticalLineLength)
                {
                    dt.Rows.Add(ballIndex, ballIndex, start, start + currentLineNumbers.Split(',').Length - 1, "Вертикальная", currentLineNumbers);
                }
            }

            // Поиск диагональных линий (слева направо и справа налево)
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 1; col <= maxBallNumber; col++)
                {
                    string currentLineNumbers = "";
                    int currentRow = row;
                    int currentCol = col;

                    // Слева направо
                    while (currentRow < rowCount && currentCol >= 1 && currentCol <= maxBallNumber)
                    {
                        string cellValue = lotteryMap[currentRow][currentCol];

                        if (cellValue != "*")
                        {
                            currentLineNumbers += (currentLineNumbers == "" ? "" : ", ") + cellValue;
                        }
                        else
                        {
                            if (currentLineNumbers.Split(',').Length >= minDiagonalLineLength)
                            {
                                dt.Rows.Add(col, col, row + 1, currentRow, "Диагональ слева направо", currentLineNumbers);
                            }
                            currentLineNumbers = "";
                        }

                        currentRow++;
                        currentCol++;
                    }

                    if (currentLineNumbers.Split(',').Length >= minDiagonalLineLength)
                    {
                        dt.Rows.Add(col, col, row + 1, currentRow, "Диагональ слева направо", currentLineNumbers);
                    }

                    // Справа налево
                    currentRow = row;
                    currentCol = col;
                    currentLineNumbers = "";

                    while (currentRow < rowCount && currentCol >= 1 && currentCol <= maxBallNumber)
                    {
                        string cellValue = lotteryMap[currentRow][currentCol];

                        if (cellValue != "*")
                        {
                            currentLineNumbers += (currentLineNumbers == "" ? "" : ", ") + cellValue;
                        }
                        else
                        {
                            if (currentLineNumbers.Split(',').Length >= minDiagonalLineLength)
                            {
                                dt.Rows.Add(col, col, row + 1, currentRow, "Диагональ справа налево", currentLineNumbers);
                            }
                            currentLineNumbers = "";
                        }

                        currentRow++;
                        currentCol--;
                    }

                    if (currentLineNumbers.Split(',').Length >= minDiagonalLineLength)
                    {
                        dt.Rows.Add(col, col, row + 1, currentRow, "Диагональ справа налево", currentLineNumbers);
                    }
                }
            }

            // Поиск фигур (пересечений)
            var horizontalLines = dt.AsEnumerable()
                .Where(row => row.Field<string>("Тип линии") == "Горизонтальная")
                .ToList();

            var verticalLines = dt.AsEnumerable()
                .Where(row => row.Field<string>("Тип линии") == "Вертикальная")
                .ToList();

            var diagonalLinesLeftToRight = dt.AsEnumerable()
                .Where(row => row.Field<string>("Тип линии") == "Диагональ слева направо")
                .ToList();

            var diagonalLinesRightToLeft = dt.AsEnumerable()
                .Where(row => row.Field<string>("Тип линии") == "Диагональ справа налево")
                .ToList();

            // Пересечение Горизонтальная + Вертикальная
            foreach (var h in horizontalLines)
            {
                foreach (var v in verticalLines)
                {
                    if (v.Field<int>("Начальная позиция") >= h.Field<int>("Начальная позиция") && v.Field<int>("Начальная позиция") <= h.Field<int>("Конечная позиция") &&
                        h.Field<int>("Начальный тираж") >= v.Field<int>("Начальный тираж") && h.Field<int>("Начальный тираж") <= v.Field<int>("Конечный тираж"))
                    {
                        int rowOffset = h.Field<int>("Начальный тираж") - v.Field<int>("Начальный тираж");
                        int colOffset = v.Field<int>("Начальная позиция") - h.Field<int>("Начальная позиция");

                        var hNumbers = h.Field<string>("Числа").Split(',').Select(s => s.Trim()).ToList();
                        var vNumbers = v.Field<string>("Числа").Split(',').Select(s => s.Trim()).ToList();

                        if (rowOffset < vNumbers.Count && colOffset < hNumbers.Count)
                        {
                            string valH = hNumbers[colOffset];
                            string valV = vNumbers[rowOffset];

                            if (valH == valV)
                            {
                                dt.Rows.Add(
                                    h.Field<int>("Начальная позиция"),
                                    h.Field<int>("Конечная позиция"),
                                    h.Field<int>("Начальный тираж"),
                                    h.Field<int>("Конечный тираж"),
                                    "Пересечение (Горизонтальная + Вертикальная)",
                                    $"Пересекающееся число: {valH} | Горизонталь: ({h.Field<string>("Числа")}) | Вертикаль: ({v.Field<string>("Числа")})"
                                );
                            }
                        }
                    }
                }
            }

            // Пересечение Вертикаль + Диагональ
            foreach (var v in verticalLines)
            {
                foreach (var d in diagonalLinesLeftToRight.Concat(diagonalLinesRightToLeft))
                {
                    if (v.Field<int>("Начальная позиция") >= d.Field<int>("Начальная позиция") && v.Field<int>("Начальная позиция") <= d.Field<int>("Конечная позиция") &&
                        d.Field<int>("Начальный тираж") >= v.Field<int>("Начальный тираж") && d.Field<int>("Начальный тираж") <= v.Field<int>("Конечный тираж"))
                    {
                        int rowOffset = d.Field<int>("Начальный тираж") - v.Field<int>("Начальный тираж");
                        int diagOffset = v.Field<int>("Начальная позиция") - d.Field<int>("Начальная позиция");

                        var vNumbers = v.Field<string>("Числа").Split(',').Select(s => s.Trim()).ToList();
                        var dNumbers = d.Field<string>("Числа").Split(',').Select(s => s.Trim()).ToList();

                        if (rowOffset < vNumbers.Count && diagOffset < dNumbers.Count && rowOffset >= 0 && diagOffset >= 0)
                        {
                            string valV = vNumbers[rowOffset];
                            string valD = dNumbers[diagOffset];

                            if (valV == valD)
                            {
                                dt.Rows.Add(
                                    v.Field<int>("Начальная позиция"),
                                    v.Field<int>("Конечная позиция"),
                                    v.Field<int>("Начальный тираж"),
                                    v.Field<int>("Конечный тираж"),
                                    "Пересечение (Вертикаль + Диагональ)",
                                    $"Пересекающееся число: {valV} | Вертикаль: ({v.Field<string>("Числа")}) | Диагональ: ({d.Field<string>("Числа")})"
                                );
                            }
                        }
                    }
                }
            }

            // Пересечение Горизонталь + Диагональ
            foreach (var h in horizontalLines)
            {
                foreach (var d in diagonalLinesLeftToRight.Concat(diagonalLinesRightToLeft))
                {
                    if (h.Field<int>("Начальный тираж") >= d.Field<int>("Начальный тираж") && h.Field<int>("Начальный тираж") <= d.Field<int>("Конечный тираж") &&
                        d.Field<int>("Начальная позиция") >= h.Field<int>("Начальная позиция") && d.Field<int>("Начальная позиция") <= h.Field<int>("Конечная позиция"))
                    {
                        int colOffset = d.Field<int>("Начальная позиция") - h.Field<int>("Начальная позиция");
                        int diagOffset = h.Field<int>("Начальный тираж") - d.Field<int>("Начальный тираж");

                        var hNumbers = h.Field<string>("Числа").Split(',').Select(s => s.Trim()).ToList();
                        var dNumbers = d.Field<string>("Числа").Split(',').Select(s => s.Trim()).ToList();

                        if (colOffset < hNumbers.Count && diagOffset < dNumbers.Count && colOffset >= 0 && diagOffset >= 0)
                        {
                            string valH = hNumbers[colOffset];
                            string valD = dNumbers[diagOffset];

                            if (valH == valD)
                            {
                                dt.Rows.Add(
                                    h.Field<int>("Начальная позиция"),
                                    h.Field<int>("Конечная позиция"),
                                    h.Field<int>("Начальный тираж"),
                                    h.Field<int>("Конечный тираж"),
                                    "Пересечение (Горизонталь + Диагональ)",
                                    $"Пересекающееся число: {valH} | Горизонталь: ({h.Field<string>("Числа")}) | Диагональ: ({d.Field<string>("Числа")})"
                                );
                            }
                        }
                    }
                }
            }

            // Пересечение Диагональ + Диагональ
            foreach (var d1 in diagonalLinesLeftToRight)
            {
                foreach (var d2 in diagonalLinesRightToLeft)
                {
                    var inter = FindIntersectionPoint(d1, d2);
                    if (inter != null)
                    {
                        int offset1 = inter.Value.Row - d1.Field<int>("Начальный тираж");
                        int offset2 = inter.Value.Row - d2.Field<int>("Начальный тираж");

                        var d1Numbers = d1.Field<string>("Числа").Split(',').Select(s => s.Trim()).ToList();
                        var d2Numbers = d2.Field<string>("Числа").Split(',').Select(s => s.Trim()).ToList();

                        if (offset1 >= 0 && offset1 < d1Numbers.Count &&
                            offset2 >= 0 && offset2 < d2Numbers.Count)
                        {
                            string val1 = d1Numbers[offset1];
                            string val2 = d2Numbers[offset2];

                            if (val1 == val2)
                            {
                                dt.Rows.Add(
                                    d1.Field<int>("Начальная позиция"),
                                    d1.Field<int>("Конечная позиция"),
                                    d1.Field<int>("Начальный тираж"),
                                    d1.Field<int>("Конечный тираж"),
                                    "Пересечение (Диагональ + Диагональ)",
                                    $"Пересекающееся число: {val1} | Диагональ 1: ({d1.Field<string>("Числа")}) | Диагональ 2: ({d2.Field<string>("Числа")})"
                                );
                            }
                        }
                    }
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A64_Horizontal_Vertical_Diagonal_Lines_And_Intersections" : title;
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

        private (int Row, int Col)? FindIntersectionPoint(DataRow dLine1, DataRow dLine2)
        {
            // Уравнения диагоналей:
            // Для dLine1 (слева направо): Row - StartRow = Col - StartCol
            // Для dLine2 (справа налево): Row - StartRow = -(Col - StartCol)

            // Решаем систему уравнений для нахождения точки пересечения
            int A1 = 1, B1 = -1, C1 = dLine1.Field<int>("Начальный тираж") - dLine1.Field<int>("Начальная позиция");
            int A2 = 1, B2 = 1, C2 = dLine2.Field<int>("Начальный тираж") + dLine2.Field<int>("Начальная позиция");

            int determinant = A1 * B2 - A2 * B1;

            if (determinant == 0)
            {
                // Диагонали параллельны, пересечения нет
                return null;
            }

            int row = (B2 * C1 - B1 * C2) / determinant;
            int col = (A1 * C2 - A2 * C1) / determinant;

            // Проверяем, что точка пересечения находится в пределах обеих диагоналей
            if (row >= dLine1.Field<int>("Начальный тираж") && row <= dLine1.Field<int>("Конечный тираж") &&
                row >= dLine2.Field<int>("Начальный тираж") && row <= dLine2.Field<int>("Конечный тираж") &&
                col >= dLine1.Field<int>("Начальная позиция") && col <= dLine1.Field<int>("Конечная позиция") &&
                col >= dLine2.Field<int>("Начальная позиция") && col <= dLine2.Field<int>("Конечная позиция"))
            {
                return (row, col);
            }

            return null;
        }
    }
}