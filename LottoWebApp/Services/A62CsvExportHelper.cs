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
    public class A62CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A62CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, int minLineLength, string? lineType, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            if (minLineLength < 2) minLineLength = 2;
            if (string.IsNullOrEmpty(lineType)) lineType = "leftToRight";

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
            dt.Columns.Add("Начальный тираж", typeof(int));
            dt.Columns.Add("Конечный тираж", typeof(int));
            dt.Columns.Add("Начальная позиция", typeof(int));
            dt.Columns.Add("Конечная позиция", typeof(int));
            dt.Columns.Add("Числа", typeof(string));

            int rowCount = lotteryMap.Count;

            for (int row = 0; row < rowCount; row++)
            {
                for (int ball = 1; ball <= maxBallNumber; ball++)
                {
                    string cellValue = lotteryMap[row][ball];

                    if (cellValue != "*" && int.TryParse(cellValue, out int currentNumber))
                    {
                        var diagonalNumbers = new List<string> { cellValue };

                        int nextNumber = (lineType == "leftToRight") ? currentNumber + 1 : currentNumber - 1;
                        int currentRow = row + 1;
                        int currentBall = ball + ((lineType == "leftToRight") ? 1 : -1);

                        // Сохраняем последние валидные координаты
                        int lastValidRow = row;
                        int lastValidBall = ball;

                        while (currentRow < rowCount && currentBall >= 1 && currentBall <= maxBallNumber)
                        {
                            string nextCell = lotteryMap[currentRow][currentBall];

                            if (nextCell != "*" &&
                                int.TryParse(nextCell, out int nextValue) &&
                                nextValue == nextNumber)
                            {
                                diagonalNumbers.Add(nextCell);
                                nextNumber = (lineType == "leftToRight") ? nextNumber + 1 : nextNumber - 1;

                                lastValidRow = currentRow;
                                lastValidBall = currentBall;

                                currentRow++;
                                currentBall += (lineType == "leftToRight" ? 1 : -1);
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (diagonalNumbers.Count >= minLineLength)
                        {
                            dt.Rows.Add(
                                row + 1,
                                lastValidRow + 1,
                                ball,
                                lastValidBall,
                                string.Join(", ", diagonalNumbers)
                            );
                        }
                    }
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? (lineType == "leftToRight" ? "A62_Diagonal_LeftToRight" : "A62_Diagonal_RightToLeft") : title;
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