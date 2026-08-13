using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A111CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A111CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? title, string? selectedTable = null)
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
            bool is320 = game.Equals("320", StringComparison.OrdinalIgnoreCase);

            string exportField = null;
            if (is320)
            {
                exportField = selectedTable?.ToLowerInvariant() switch
                {
                    "b1" => "B1",
                    "b2" => "B2",
                    _ => "B1"
                };
            }

            var propNames = is320
                ? new[] { $"{exportField}1", $"{exportField}2", $"{exportField}3" }
                : first.GetType().GetProperties()
                    .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                    .OrderBy(p => int.Parse(p.Name.Substring(1)))
                    .Select(p => p.Name)
                    .ToArray();

            if (!propNames.Any())
                throw new InvalidOperationException("Не найдены столбцы чисел.");

            var positionNumberCounts = new Dictionary<int, Dictionary<int, int>>();
            var totalDrawsPerPosition = new int[propNames.Length];

            foreach (var row in draws)
            {
                for (int i = 0; i < propNames.Length; i++)
                {
                    var prop = row.GetType().GetProperty(propNames[i]);
                    if (prop == null) continue;

                    var val = prop.GetValue(row);
                    if (val == null) continue;

                    if (int.TryParse(val.ToString(), out int number) && number != 0)
                    {
                        if (!positionNumberCounts.TryGetValue(i + 1, out var numberCounts))
                        {
                            numberCounts = new Dictionary<int, int>();
                            positionNumberCounts[i + 1] = numberCounts;
                        }

                        if (numberCounts.ContainsKey(number))
                            numberCounts[number]++;
                        else
                            numberCounts.Add(number, 1);

                        totalDrawsPerPosition[i]++;
                    }
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Позиция");
            dt.Columns.Add("№ Шара");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");

            foreach (var position in positionNumberCounts.Keys.OrderBy(p => p))
            {
                var numberCounts = positionNumberCounts[position];
                int totalDraws = totalDrawsPerPosition[position - 1];
                if (totalDraws == 0) continue;

                var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();

                foreach (var number in sortedNumbers)
                {
                    double chance = Math.Round((double)number.Value / totalDraws, 4);
                    dt.Rows.Add($"Позиция {position}", number.Key.ToString(), number.Value.ToString(), chance.ToString());
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A111_Position_Frequency" : title;
            string gameStr = game ?? "unknown";
            string drawCountStr = drawCount?.ToString() ?? "all";
            string fieldStr = is320 ? $"_{exportField}" : "";

            string safeFileName = $"{titlePart}_{gameStr}{fieldStr}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
                .Replace(" ", "_").Replace(":", "_").Replace("/", "_").Replace("\\", "_");

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