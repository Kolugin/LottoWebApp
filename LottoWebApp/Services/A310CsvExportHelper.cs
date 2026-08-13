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
    public class A310CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A310CsvExportHelper(LottoDbContext db)
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
            int maxBallNumber = game?.ToLower() switch
            {
                "keno" => 60,
                "blitz" => 20,
                "5x36" => 36,
                "6x49" => 49,
                "1224" => 24,
                _ => 60
            };

            int ballsPerDraw = game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            (int rows, int cols) = game?.ToLower() switch
            {
                "keno" => (6, 10), // 60 = 6 * 10
                "blitz" => (4, 5), // 20 = 4 * 5
                "5x36" => (6, 6), // 36 = 6 * 6
                "6x49" => (7, 7), // 49 = 7 * 7
                "1224" => (4, 6), // 24 = 4 * 6
                _ => (6, 10)
            };

            var rowDistribution = new Dictionary<int, Dictionary<(int Even, int Odd), int>>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var matrix = BuildMatrixFromRow(row, numberProps, rows, cols, ballsPerDraw);

                for (int r = 0; r < rows; r++)
                {
                    int even = 0, odd = 0;
                    for (int c = 0; c < cols; c++)
                    {
                        int val = matrix[r, c];
                        if (val != -1)
                        {
                            if (val % 2 == 0) even++;
                            else odd++;
                        }
                    }

                    var key = (even, odd);

                    if (!rowDistribution.ContainsKey(r + 1))
                        rowDistribution[r + 1] = new Dictionary<(int, int), int>();

                    if (!rowDistribution[r + 1].ContainsKey(key))
                        rowDistribution[r + 1][key] = 0;

                    rowDistribution[r + 1][key]++;
                    totalDraws++;
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Строка", typeof(int));
            dt.Columns.Add("Четные", typeof(int));
            dt.Columns.Add("Нечетные", typeof(int));
            dt.Columns.Add("Количество", typeof(int));
            dt.Columns.Add("Шанс", typeof(double));

            foreach (var entry in rowDistribution)
            {
                foreach (var pair in entry.Value)
                {
                    double chance = totalDraws > 0
                        ? Math.Round((double)pair.Value / totalDraws, 4)
                        : 0.0;

                    dt.Rows.Add(entry.Key, pair.Key.Even, pair.Key.Odd, pair.Value, chance);
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A310_Row_Distribution" : title;
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

        private int[,] BuildMatrixFromRow(object row, List<System.Reflection.PropertyInfo> numberProps, int rows, int cols, int ballsPerDraw)
        {
            var matrix = new int[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    matrix[r, c] = -1;

            var drawnNumbers = new List<int>();
            for (int i = 1; i <= ballsPerDraw; i++)
            {
                var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                if (prop != null)
                {
                    var val = prop.GetValue(row);
                    if (val != null && int.TryParse(val.ToString(), out int num) && num > 0 && num <= rows * cols)
                    {
                        drawnNumbers.Add(num);
                    }
                }
            }

            foreach (var num in drawnNumbers)
            {
                int r = (num - 1) / cols;
                int c = (num - 1) % cols;
                matrix[r, c] = num;
            }

            return matrix;
        }
    }
}