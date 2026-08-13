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
    public class A36CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A36CsvExportHelper(LottoDbContext db)
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

            var evenOddDistribution = new Dictionary<int, int>();
            for (int i = 0; i <= numberOfBalls; i++)
            {
                evenOddDistribution[i] = 0;
            }

            int totalDraws = 0;

            foreach (var row in draws)
            {
                int evenCount = 0;

                for (int j = 1; j <= numberOfBalls; j++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int currentBall) && currentBall != 0 && currentBall % 2 == 0)
                        {
                            evenCount++;
                        }
                    }
                }

                int oddCount = numberOfBalls - evenCount;
                if (evenCount + oddCount > 0)
                {
                    evenOddDistribution[evenCount]++;
                    totalDraws++;
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Кол-во четных", typeof(int));
            dt.Columns.Add("Кол-во нечетных", typeof(int));
            dt.Columns.Add("Всего выпало", typeof(int));
            dt.Columns.Add("Шанс (%)", typeof(double));

            foreach (var entry in evenOddDistribution)
            {
                int evenNumbers = entry.Key;
                int oddNumbers = numberOfBalls - evenNumbers;
                int count = entry.Value;

                double chance = totalDraws > 0 ? Math.Round((double)count / totalDraws * 100, 4) : 0.0;

                dt.Rows.Add(evenNumbers, oddNumbers, count, chance);
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A36_Even_Odd_Distribution" : title;
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