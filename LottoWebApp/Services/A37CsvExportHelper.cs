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
    public class A37CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A37CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

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

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any())
                throw new InvalidOperationException("Не найдены столбцы чисел.");

            // Определяем количество шаров для игры
            int numberOfBalls = game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            // Интервалы для анализа (10, 50 и 100 тиражей)
            var intervals = new List<int> { 10, 50, 100 };

            foreach (int interval in intervals)
            {
                var dt = new DataTable();
                dt.Columns.Add("№ Тиражей", typeof(string));
                for (int i = 1; i <= numberOfBalls; i++)
                {
                    dt.Columns.Add($"№Поз {i}", typeof(string));
                }

                var ballCounts = new Dictionary<int, int>();

                for (int startIndex = 0; startIndex <= draws.Count - interval; startIndex++)
                {
                    ballCounts.Clear();
                    var row = dt.NewRow();
                    row["№ Тиражей"] = $"{startIndex + 1}_{startIndex + interval}";

                    // Подсчет выпадений для каждого шара в текущем окне
                    for (int i = startIndex; i < startIndex + interval; i++)
                    {
                        var currentRow = draws[i];
                        for (int colIndex = 0; colIndex < numberProps.Count; colIndex++)
                        {
                            var prop = numberProps[colIndex];
                            var val = prop.GetValue(currentRow);
                            if (val != null && int.TryParse(val.ToString(), out int ball) && ball != 0)
                            {
                                if (!ballCounts.ContainsKey(ball))
                                    ballCounts[ball] = 0;
                                ballCounts[ball]++;
                            }
                        }
                    }

                    // Запись вероятностей для каждого шара в текущем интервале
                    for (int position = 1; position <= numberOfBalls; position++)
                    {
                        if (startIndex + position - 1 < draws.Count)
                        {
                            var currentDraw = draws[startIndex + position - 1];
                            var positionProp = numberProps.FirstOrDefault(p => p.Name == $"B{position}");
                            if (positionProp != null)
                            {
                                var val = positionProp.GetValue(currentDraw);
                                if (val != null && int.TryParse(val.ToString(), out int ball))
                                {
                                    if (ballCounts.ContainsKey(ball))
                                    {
                                        double probability = (double)interval / ballCounts[ball];
                                        row[$"№Поз {position}"] = probability.ToString("F8");
                                    }
                                    else
                                    {
                                        row[$"№Поз {position}"] = "-";
                                    }
                                }
                            }
                        }
                        else
                        {
                            row[$"№Поз {position}"] = "-";
                        }
                    }

                    dt.Rows.Add(row);
                }

                string titlePart = string.IsNullOrEmpty(title) ? "A37_Frequency_Distribution_Map" : title;
                string gameStr = game ?? "unknown";
                string drawCountStr = drawCount?.ToString() ?? "all";

                string safeFileName = $"{titlePart}_{gameStr}_{drawCountStr}_{interval}_draws_{DateTime.Now:yyyyMMddHHmmss}.csv"
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

            // Возвращаем последний файл
            string lastFileName = $"A37_Frequency_Distribution_Map_{game}_{drawCount?.ToString() ?? "all"}_100_draws_{DateTime.Now:yyyyMMddHHmmss}.csv";
            string lastFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", lastFileName);

            var lastBytes = await System.IO.File.ReadAllBytesAsync(lastFilePath);
            return new FileContentResult(lastBytes, "text/csv") { FileDownloadName = lastFileName };
        }
    }
}