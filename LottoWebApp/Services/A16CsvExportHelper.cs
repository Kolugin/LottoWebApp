using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A16CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A16CsvExportHelper(LottoDbContext db)
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

            var monthCounts = new Dictionary<string, Dictionary<int, int>>();
            var monthTotal = new Dictionary<string, int>();

            foreach (var row in draws)
            {
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                string monthKey = date.ToString("MMMM", CultureInfo.GetCultureInfo("ru-RU"));

                if (!monthCounts.ContainsKey(monthKey))
                {
                    monthCounts[monthKey] = new Dictionary<int, int>();
                    monthTotal[monthKey] = 0;
                }

                foreach (var propName in propNames)
                {
                    var prop = row.GetType().GetProperty(propName);
                    if (prop == null) continue;

                    var val = prop.GetValue(row);
                    if (val == null) continue;

                    if (int.TryParse(val.ToString(), out int number) && number != 0)
                    {
                        if (monthCounts[monthKey].ContainsKey(number))
                            monthCounts[monthKey][number]++;
                        else
                            monthCounts[monthKey][number] = 1;

                        monthTotal[monthKey]++;
                    }
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Месяц");
            dt.Columns.Add("№ Шара");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");
            dt.Columns.Add("Тип");

            var sortedMonths = monthCounts.Keys
                .Select(m =>
                {
                    var monthDate = DateTime.ParseExact(m + " 2000", "MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU")).Month;
                    return (MonthName: m, MonthNumber: monthDate);
                })
                .OrderBy(m => m.MonthNumber)
                .Select(m => m.MonthName)
                .ToList();

            foreach (var month in sortedMonths)
            {
                if (!monthCounts.ContainsKey(month) || !monthTotal.ContainsKey(month))
                    continue;

                var numbers = monthCounts[month];
                int total = monthTotal[month];
                if (total == 0) continue;

                var frequent = numbers.OrderByDescending(n => n.Value).Take(10).ToList();
                var rare = numbers.OrderBy(n => n.Value).Take(10).ToList();

                foreach (var (number, count) in frequent)
                {
                    double chance = Math.Round((double)count / total, 4);
                    dt.Rows.Add(month, number.ToString(), count.ToString(), chance.ToString(), "Частое");
                }

                foreach (var (number, count) in rare)
                {
                    double chance = Math.Round((double)count / total, 4);
                    dt.Rows.Add(month, number.ToString(), count.ToString(), chance.ToString(), "Редкое");
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A16_Frequency_By_Month" : title;
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