using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A112CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A112CsvExportHelper(LottoDbContext db)
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

            // Ищем свойство BB (дополнительный шар)
            var bonusBallProp = first.GetType().GetProperty("BB");
            if (bonusBallProp == null)
                throw new InvalidOperationException("Не найдено поле 'BB'.");

            var numberCounts = new Dictionary<int, int>();

            // Обработка данных
            foreach (var row in draws)
            {
                var val = bonusBallProp.GetValue(row);
                if (val == null) continue;

                if (int.TryParse(val.ToString(), out int number) && number != 0)
                {
                    if (numberCounts.TryGetValue(number, out int count))
                    {
                        numberCounts[number] = count + 1;
                    }
                    else
                    {
                        numberCounts.Add(number, 1);
                    }
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("№ Шара");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");

            // Получаем общее количество выпадений
            int totalDraws = numberCounts.Values.Sum();
            var sortedNumbers = numberCounts.OrderByDescending(n => n.Value).ToList();

            foreach (var number in sortedNumbers)
            {
                double chance = Math.Round((double)number.Value / totalDraws * 100, 4);
                dt.Rows.Add(number.Key.ToString(), number.Value.ToString(), chance.ToString());
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A112_Bonus_Ball_Frequency" : title;
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