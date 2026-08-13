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
    public class A61CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A61CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, int minLineLength, string? lineType, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            if (minLineLength < 2) minLineLength = 2;
            if (string.IsNullOrEmpty(lineType)) lineType = "horizontal";

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

                // Заполняем числа
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
            if (lineType == "horizontal")
            {
                dt.Columns.Add("№ Тиража", typeof(int));
                dt.Columns.Add("Начальная позиция", typeof(int));
                dt.Columns.Add("Конечная позиция", typeof(int));
                dt.Columns.Add("Числа", typeof(string));
            }
            else
            {
                dt.Columns.Add("№ Позиции", typeof(int));
                dt.Columns.Add("Начальный тираж", typeof(int));
                dt.Columns.Add("Конечный тираж", typeof(int));
                dt.Columns.Add("Числа", typeof(string));
            }

            if (lineType == "horizontal")
            {
                // Поиск горизонтальных линий
                for (int rowIndex = 0; rowIndex < lotteryMap.Count; rowIndex++)
                {
                    var row = lotteryMap[rowIndex];
                    int start = -1;
                    var currentLineValues = new List<string>();

                    for (int ball = 1; ball <= maxBallNumber; ball++)
                    {
                        string cellValue = row[ball];

                        if (cellValue != "*")
                        {
                            if (start == -1)
                                start = ball; // Начальная позиция - это номер шара

                            currentLineValues.Add(cellValue);
                        }
                        else
                        {
                            if (currentLineValues.Count >= minLineLength)
                            {
                                dt.Rows.Add(rowIndex + 1, start, start + currentLineValues.Count - 1, string.Join(", ", currentLineValues));
                            }
                            start = -1;
                            currentLineValues.Clear();
                        }
                    }

                    // Проверка на конец строки
                    if (currentLineValues.Count >= minLineLength)
                    {
                        dt.Rows.Add(rowIndex + 1, start, start + currentLineValues.Count - 1, string.Join(", ", currentLineValues));
                    }
                }
            }
            else
            {
                // Поиск вертикальных линий
                for (int ballIndex = 1; ballIndex <= maxBallNumber; ballIndex++)
                {
                    int start = -1;
                    var currentLineValues = new List<string>();

                    for (int row = 0; row < lotteryMap.Count; row++)
                    {
                        string cellValue = lotteryMap[row][ballIndex];

                        if (cellValue != "*")
                        {
                            if (start == -1)
                                start = row + 1; // Начальный тираж

                            currentLineValues.Add(cellValue);
                        }
                        else
                        {
                            if (currentLineValues.Count >= minLineLength)
                            {
                                dt.Rows.Add(ballIndex, start, start + currentLineValues.Count - 1, string.Join(", ", currentLineValues));
                            }
                            start = -1;
                            currentLineValues.Clear();
                        }
                    }

                    // Проверка на конец столбца
                    if (currentLineValues.Count >= minLineLength)
                    {
                        dt.Rows.Add(ballIndex, start, start + currentLineValues.Count - 1, string.Join(", ", currentLineValues));
                    }
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? (lineType == "horizontal" ? "A61_Horizontal_Lines" : "A62_Vertical_Lines") : title;
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