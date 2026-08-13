using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A15CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A15CsvExportHelper(LottoDbContext db)
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
            var dateProp = first.GetType().GetProperty("Date");
            if (dateProp == null)
                throw new InvalidOperationException("Не найдено поле 'Date'.");

            bool is320 = game.Equals("320", StringComparison.OrdinalIgnoreCase);

            // Определяем, какое поле экспортировать
            string exportField = null;
            if (is320)
            {
                exportField = selectedTable?.ToLowerInvariant() switch
                {
                    "b1" => "B1",
                    "b2" => "B2",
                    _ => "B1" // по умолчанию
                };
            }

            // Получаем список свойств для обработки
            var numberPropNames = is320
                ? new[] { $"{exportField}1", $"{exportField}2", $"{exportField}3" }
                : first.GetType().GetProperties()
                    .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                    .OrderBy(p => int.Parse(p.Name.Substring(1)))
                    .Select(p => p.Name)
                    .ToArray();

            if (!numberPropNames.Any())
                throw new InvalidOperationException("Не найдены столбцы чисел.");

            int maxNumber = is320 ? 20 : 60;

            // Структура для хранения данных: день недели -> [число -> количество]
            var dayOfWeekCounts = new Dictionary<string, int[]>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var row in draws)
            {
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(
                    dateValue.ToString(),
                    "dd.MM.yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
                {
                    continue;
                }

                string dayOfWeek = date.ToString("dddd", CultureInfo.GetCultureInfo("ru-RU"));

                if (!dayOfWeekCounts.ContainsKey(dayOfWeek))
                    dayOfWeekCounts[dayOfWeek] = new int[maxNumber];

                foreach (var propName in numberPropNames)
                {
                    var prop = row.GetType().GetProperty(propName);
                    if (prop == null) continue;

                    var val = prop.GetValue(row);
                    if (val == null) continue;

                    if (int.TryParse(val.ToString(), out int number) && number > 0 && number <= maxNumber)
                        dayOfWeekCounts[dayOfWeek][number - 1]++;
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("День недели");
            dt.Columns.Add("№ Шара");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");
            dt.Columns.Add("Тип");

            foreach (var dayEntry in dayOfWeekCounts)
            {
                string day = dayEntry.Key;
                int[] counts = dayEntry.Value;
                int totalCountForDay = counts.Sum();

                if (totalCountForDay == 0) continue;

                var frequentNumbers = counts
                    .Select((count, index) => new { Number = index + 1, Count = count })
                    .Where(n => n.Count > 0)
                    .OrderByDescending(n => n.Count)
                    .Take(10)
                    .ToList();

                var rareNumbers = counts
                    .Select((count, index) => new { Number = index + 1, Count = count })
                    .Where(n => n.Count > 0)
                    .OrderBy(n => n.Count)
                    .Take(10)
                    .ToList();

                foreach (var num in frequentNumbers)
                {
                    double frequency = Math.Round((double)num.Count / totalCountForDay, 4);
                    dt.Rows.Add(day, num.Number.ToString(), num.Count.ToString(), frequency.ToString(), "Частое");
                }

                foreach (var num in rareNumbers)
                {
                    double frequency = Math.Round((double)num.Count / totalCountForDay, 4);
                    dt.Rows.Add(day, num.Number.ToString(), num.Count.ToString(), frequency.ToString(), "Редкое");
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A15_Frequency_By_Weekday" : title;
            string gameStr = game ?? "unknown";
            string drawCountStr = drawCount?.ToString() ?? "all";
            string fieldStr = is320 ? $"_{exportField}" : "";

            string safeFileName = $"{titlePart}_{gameStr}{fieldStr}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
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