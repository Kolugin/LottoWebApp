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
    public class A320CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A320CsvExportHelper(LottoDbContext db)
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

            // Определяем параметры для игры
            int maxBallNumber, numberOfBalls;
            bool hasBonus;
            (maxBallNumber, numberOfBalls, hasBonus) = game?.ToLower() switch
            {
                "keno" => (60, 20, false),
                "blitz" => (20, 8, true),
                "5x36" => (36, 6, false),
                "6x49" => (49, 6, false),
                "1224" => (24, 12, false),
                _ => (60, 20, false)
            };

            int maxPause = 30;
            int[,] pauseFrequencies = new int[maxBallNumber + 1, maxPause + 1];
            int[,] pauseOccurrences = new int[maxBallNumber + 1, maxPause + 1];
            int[] currentPauses = new int[maxBallNumber + 1];

            int bonusCount = 4;
            int[,] bonusFrequencies = new int[bonusCount + 1, maxPause + 1];
            int[,] bonusOccurrences = new int[bonusCount + 1, maxPause + 1];
            int[] bonusPauses = new int[bonusCount + 1];

            foreach (var row in draws)
            {
                var drawn = new HashSet<int>();
                for (int i = 1; i <= numberOfBalls; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                        {
                            drawn.Add(num);
                        }
                    }
                }

                // Основные шары
                for (int ball = 1; ball <= maxBallNumber; ball++)
                {
                    int pause = currentPauses[ball];
                    int clipped = Math.Min(pause, maxPause);
                    pauseOccurrences[ball, clipped]++;

                    if (drawn.Contains(ball))
                    {
                        pauseFrequencies[ball, clipped]++;
                        currentPauses[ball] = 0;
                    }
                    else
                    {
                        currentPauses[ball]++;
                    }
                }

                // Бонусные шары
                if (hasBonus)
                {
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var bbVal = bbProp.GetValue(row);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int bonusBall))
                        {
                            for (int b = 1; b <= bonusCount; b++)
                            {
                                int pause = bonusPauses[b];
                                int clipped = Math.Min(pause, maxPause);
                                bonusOccurrences[b, clipped]++;

                                if (bonusBall == b)
                                {
                                    bonusFrequencies[b, clipped]++;
                                    bonusPauses[b] = 0;
                                }
                                else
                                {
                                    bonusPauses[b]++;
                                }
                            }
                        }
                    }
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Шар", typeof(string));
            dt.Columns.Add("Количество пауз", typeof(int));
            dt.Columns.Add("Кол-во выпадений", typeof(int));
            dt.Columns.Add("Шанс %", typeof(double));

            // Основная таблица
            for (int ball = 1; ball <= maxBallNumber; ball++)
            {
                for (int pause = 0; pause <= maxPause; pause++)
                {
                    int hits = pauseFrequencies[ball, pause];
                    int total = pauseOccurrences[ball, pause];
                    if (total == 0 || hits == 0) continue;

                    double chance = Math.Round((double)hits / total, 4);

                    dt.Rows.Add($"№{ball}", pause, hits, chance);
                }
            }

            // Бонусные шары
            if (hasBonus)
            {
                for (int b = 1; b <= bonusCount; b++)
                {
                    for (int pause = 0; pause <= maxPause; pause++)
                    {
                        int hits = bonusFrequencies[b, pause];
                        int total = bonusOccurrences[b, pause];
                        if (total == 0 || hits == 0) continue;

                        double chance = Math.Round((double)hits / total, 4);

                        dt.Rows.Add($"Бонус {b}", pause, hits, chance);
                    }
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A320_Pause_Distribution" : title;
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