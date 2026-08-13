using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A23CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A23CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            var query = new LotteryQueryProvider(_db).GetQuery(game);

            // Для правильного расчета пауз всегда сортируем по возрастанию номера тиража
            query = query.OrderBy(e => EF.Property<int>(e, "Draw"));

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
            int totalBalls = 0;
            int ballCount = 0;
            bool hasTime = false;
            bool hasBonus = false;

            switch (game?.ToLower())
            {
                case "keno":
                    totalBalls = 60;
                    ballCount = 20;
                    hasTime = false;
                    hasBonus = false;
                    break;
                case "blitz":
                    totalBalls = 20;
                    ballCount = 8;
                    hasTime = true;
                    hasBonus = true;
                    break;
                case "5x36":
                    totalBalls = 36;
                    ballCount = 6;
                    hasTime = false;
                    hasBonus = false;
                    break;
                case "6x49":
                    totalBalls = 49;
                    ballCount = 6;
                    hasTime = false;
                    hasBonus = false;
                    break;
                case "1224":
                    totalBalls = 24;
                    ballCount = 12;
                    hasTime = true;
                    hasBonus = false;
                    break;
                default:
                    totalBalls = 60;
                    ballCount = 20;
                    hasTime = false;
                    hasBonus = false;
                    break;
            }

            int[] pauseCounters = new int[totalBalls + 1];
            int[] bonusPauseCounters = new int[5]; // для 1..4 бонусных

            // Создаем DataTable
            DataTable dt = new DataTable();
            dt.Columns.Add("Тираж");
            dt.Columns.Add("Дата");
            if (hasTime)
            {
                dt.Columns.Add("Время");
            }
            for (int i = 1; i <= totalBalls; i++)
            {
                dt.Columns.Add($"№{i}");
            }

            // Добавляем колонки бонусов, если нужно
            if (hasBonus)
            {
                for (int b = 1; b <= 4; b++)
                {
                    dt.Columns.Add($"Бонус {b}");
                }
            }

            var tempRows = new List<object[]>();

            foreach (var rowObj in draws)
            {
                var rowType = rowObj.GetType();
                var drawProp = rowType.GetProperty("Draw");
                var dateProp = rowType.GetProperty("Date");
                var timeProp = hasTime ? rowType.GetProperty("Time") : null;

                if (drawProp == null || dateProp == null) continue;

                var drawValue = drawProp.GetValue(rowObj);
                var dateValue = dateProp.GetValue(rowObj);
                var timeValue = hasTime && timeProp != null ? timeProp.GetValue(rowObj) : null;

                // Выпавшие основные шары
                var drawnBalls = new HashSet<int>();
                for (int i = 1; i <= ballCount; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(rowObj);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                        {
                            drawnBalls.Add(num);
                        }
                    }
                }

                // Выпавший бонус
                int bonusBall = 0;
                if (hasBonus)
                {
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var val = bbProp.GetValue(rowObj);
                        if (val != null && int.TryParse(val.ToString(), out int num))
                        {
                            bonusBall = num;
                        }
                    }
                }

                var newRow = new object[dt.Columns.Count];
                newRow[0] = drawValue?.ToString() ?? "";
                newRow[1] = dateValue?.ToString() ?? "";
                int colIndex = 2;
                if (hasTime)
                {
                    newRow[colIndex] = timeValue?.ToString() ?? "";
                    colIndex++;
                }

                // Паузы по основным шарам
                for (int ball = 1; ball <= totalBalls; ball++)
                {
                    if (drawnBalls.Contains(ball))
                        pauseCounters[ball] = 0;
                    else
                        pauseCounters[ball]++;

                    newRow[colIndex] = pauseCounters[ball].ToString();
                    colIndex++;
                }

                // Паузы по бонусу (обычно 1–4)
                if (hasBonus)
                {
                    for (int b = 1; b <= 4; b++)
                    {
                        if (bonusBall == b)
                            bonusPauseCounters[b] = 0;
                        else
                            bonusPauseCounters[b]++;

                        newRow[colIndex] = bonusPauseCounters[b].ToString();
                        colIndex++;
                    }
                }

                tempRows.Add(newRow);
            }

            // Добавляем строки в правильном порядке
            bool isDesc = direction == "newToOld";
            if (isDesc)
            {
                // Если изначально было сортировка по убыванию, то добавляем в обратном порядке
                foreach (var row in tempRows.AsEnumerable().Reverse())
                {
                    dt.Rows.Add(row);
                }
            }
            else
            {
                foreach (var row in tempRows)
                {
                    dt.Rows.Add(row);
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A23_Ball_Pause_Map" : title;
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