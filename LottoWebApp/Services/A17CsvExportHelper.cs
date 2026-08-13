using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A17CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A17CsvExportHelper(LottoDbContext db)
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

            var dayOfMonthNumberCounts = new Dictionary<int, Dictionary<int, int>>();
            var dayOfMonthTotalDraws = new Dictionary<int, int>();

            foreach (var row in draws)
            {
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                int dayOfMonth = date.Day;
                if (!dayOfMonthNumberCounts.ContainsKey(dayOfMonth))
                {
                    dayOfMonthNumberCounts[dayOfMonth] = new Dictionary<int, int>();
                    dayOfMonthTotalDraws[dayOfMonth] = 0;
                }

                var numbers = propNames.Select(name =>
                {
                    var prop = row.GetType().GetProperty(name);
                    if (prop == null) return 0;
                    var val = prop.GetValue(row);
                    if (val != null && int.TryParse(val.ToString(), out int num) && num != 0)
                        return num;
                    return 0;
                }).Where(n => n != 0);

                foreach (var number in numbers)
                {
                    if (dayOfMonthNumberCounts[dayOfMonth].ContainsKey(number))
                        dayOfMonthNumberCounts[dayOfMonth][number]++;
                    else
                        dayOfMonthNumberCounts[dayOfMonth][number] = 1;
                }
                dayOfMonthTotalDraws[dayOfMonth]++;
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("День месяца");
            dt.Columns.Add("№ Шара");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");
            dt.Columns.Add("Тип");

            for (int day = 1; day <= 31; day++)
            {
                if (!dayOfMonthNumberCounts.ContainsKey(day)) continue;

                var numberCounts = dayOfMonthNumberCounts[day];
                int totalDraws = dayOfMonthTotalDraws[day];
                var sortedNumbers = numberCounts.OrderByDescending(n => n.Value).ToList();
                var frequentNumbers = sortedNumbers.Take(10);
                var rareNumbers = sortedNumbers.Skip(Math.Max(0, sortedNumbers.Count - 10)).Take(10);

                foreach (var number in frequentNumbers)
                {
                    double chance = Math.Round((double)number.Value / totalDraws, 4);
                    dt.Rows.Add(day.ToString(), number.Key.ToString(), number.Value.ToString(), chance.ToString(), "Частое");
                }
                foreach (var number in rareNumbers)
                {
                    double chance = Math.Round((double)number.Value / totalDraws, 4);
                    dt.Rows.Add(day.ToString(), number.Key.ToString(), number.Value.ToString(), chance.ToString(), "Редкое");
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A17_Frequency_By_DayOfMonth" : title;
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