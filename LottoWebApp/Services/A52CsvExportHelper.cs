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
    public class A52CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A52CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? title, int selectedInterval, int selectedPosition)
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
            int numberOfBalls, maxColumn;
            (numberOfBalls, maxColumn) = game?.ToLower() switch
            {
                "keno" => (60, 20),
                "blitz" => (20, 8),
                "5x36" => (36, 6),
                "6x49" => (49, 6),
                "1224" => (24, 12),
                _ => (60, 20)
            };

            int maxIntervals = 10;
            var transitionMatricesByInterval = new Dictionary<int, int[,,]>();
            for (int interval = 1; interval <= maxIntervals; interval++)
                transitionMatricesByInterval[interval] = new int[numberOfBalls, numberOfBalls, maxColumn];

            var currentDrawBalls = new List<int>();
            var futureDrawBalls = new List<int>();

            for (int i = 0; i < draws.Count - maxIntervals; i++)
            {
                currentDrawBalls.Clear();
                for (int j = 1; j <= maxColumn; j++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(draws[i]);
                        if (val != null && int.TryParse(val.ToString(), out int num))
                        {
                            currentDrawBalls.Add(num);
                        }
                    }
                }

                for (int interval = 1; interval <= maxIntervals; interval++)
                {
                    if (i + interval >= draws.Count) break;

                    futureDrawBalls.Clear();
                    for (int k = 1; k <= maxColumn; k++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{k}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(draws[i + interval]);
                            if (val != null && int.TryParse(val.ToString(), out int num))
                            {
                                futureDrawBalls.Add(num);
                            }
                        }
                    }

                    for (int pos = 0; pos < currentDrawBalls.Count; pos++)
                    {
                        int currentBall = currentDrawBalls[pos];
                        for (int futurePos = 0; futurePos < futureDrawBalls.Count; futurePos++)
                        {
                            int futureBall = futureDrawBalls[futurePos];
                            if (futureBall > 0 && futureBall <= numberOfBalls && currentBall > 0 && currentBall <= numberOfBalls)
                                transitionMatricesByInterval[interval][currentBall - 1, futureBall - 1, pos]++;
                        }
                    }
                }
            }

            // Создаем CSV для выбранного интервала и позиции
            var csvContent = new StringBuilder();
            csvContent.AppendLine($"Таблица переходов по интервалам для игры {game}, Интервал {selectedInterval} тиражей, Позиция {selectedPosition + 1}");
            csvContent.AppendLine("Число;" + string.Join(";", Enumerable.Range(1, maxColumn).Select(x => $"{x} позиция")));

            for (int number = 1; number <= numberOfBalls; number++)
            {
                var row = new List<string> { number.ToString() };
                int total = 0;
                for (int j = 0; j < numberOfBalls; j++)
                {
                    total += transitionMatricesByInterval[selectedInterval][number - 1, j, selectedPosition];
                }

                for (int futurePos = 0; futurePos < maxColumn; futurePos++)
                {
                    double prob = total > 0 ? (double)transitionMatricesByInterval[selectedInterval][number - 1, futurePos, selectedPosition] / total : 0;
                    row.Add($"{prob:F4}");
                }
                csvContent.AppendLine(string.Join(";", row));
            }

            string fileName = $"5.2_{game.ToUpper()}_Интервал_{selectedInterval}_Позиция_{selectedPosition + 1}_{DateTime.Now:yyyyMMddHHmmss}.csv";
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fileName);
            await File.WriteAllTextAsync(filePath, csvContent.ToString(), Encoding.UTF8);

            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return new FileContentResult(bytes, "text/csv") { FileDownloadName = fileName };
        }
    }
}