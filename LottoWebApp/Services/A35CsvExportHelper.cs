using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LottoWebApp.Services
{
    public class A35CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A35CsvExportHelper(LottoDbContext db)
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

            bool is320 = game.Equals("320", StringComparison.OrdinalIgnoreCase);

            int numberOfBalls;
            bool hasTime;
            (numberOfBalls, hasTime) = game?.ToLower() switch
            {
                "keno" => (20, false),
                "blitz" => (8, true),
                "5x36" => (6, false),
                "6x49" => (6, false),
                "1224" => (12, true),
                "320" => (6, true),
                _ => (20, false)
            };

            var propNames320 = new[] { "B11", "B12", "B13", "B21", "B22", "B23" };

            var resultsTable = new DataTable();
            resultsTable.Columns.Add("Тираж", typeof(int));
            resultsTable.Columns.Add("Дата", typeof(string));
            if (hasTime)
                resultsTable.Columns.Add("Время", typeof(string));

            for (int i = 1; i <= numberOfBalls; i++)
            {
                resultsTable.Columns.Add($"№П {i}", typeof(string));
            }

            int rowCount = draws.Count;

            for (int i = 0; i < rowCount; i++)
            {
                var currentRow = draws[i];
                int baseColumns = hasTime ? 3 : 2;
                var values = new object[baseColumns + numberOfBalls];

                var drawProp = first.GetType().GetProperty("Draw");
                var dateProp = first.GetType().GetProperty("Date");
                var timeProp = hasTime ? first.GetType().GetProperty("Time") : null;

                values[0] = Convert.ToInt32(drawProp?.GetValue(currentRow) ?? 0);
                values[1] = dateProp?.GetValue(currentRow)?.ToString() ?? "";

                int colIndex = 2;
                if (hasTime && timeProp != null)
                {
                    values[colIndex] = timeProp.GetValue(currentRow)?.ToString() ?? "";
                    colIndex++;
                }

                for (int j = 1; j <= numberOfBalls; j++)
                {
                    int value = 0;

                    if (is320)
                    {
                        var prop = first.GetType().GetProperty(propNames320[j - 1]);
                        if (prop != null)
                        {
                            var val = prop.GetValue(currentRow);
                            if (val != null && int.TryParse(val.ToString(), out int num))
                            {
                                value = num;
                            }
                        }
                    }
                    else
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(currentRow);
                            if (val != null && int.TryParse(val.ToString(), out int num))
                            {
                                value = num;
                            }
                        }
                    }

                    if (value == 0)
                    {
                        values[colIndex] = "-";
                    }
                    else
                    {
                        values[colIndex] = (value % 2 == 0) ? "Ч" : "Н";
                    }
                    colIndex++;
                }

                resultsTable.Rows.Add(values);
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A35_Even_Odd_Map" : title;
            string gameStr = game ?? "unknown";
            string drawCountStr = drawCount?.ToString() ?? "all";

            string safeFileName = $"{titlePart}_{gameStr}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
                .Replace(" ", "_").Replace(":", "_").Replace("/", "_").Replace("\\", "_");

            string filePath = await CsvExportService.ExportToCsvAsync(resultsTable, safeFileName);

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