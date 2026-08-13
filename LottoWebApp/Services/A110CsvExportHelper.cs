using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A110CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A110CsvExportHelper(LottoDbContext db)
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

            var culture = new CultureInfo("ru-RU");
            var dayOfWeekNames = new[] { "понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье" };

            var monthOrder = new Dictionary<string, int>
            {
                {"январь", 1}, {"февраль", 2}, {"март", 3}, {"апрель", 4}, {"май", 5}, {"июнь", 6},
                {"июль", 7}, {"август", 8}, {"сентябрь", 9}, {"октябрь", 10}, {"ноябрь", 11}, {"декабрь", 12}
            };

            var dayMonthWeekNumberCounts = new Dictionary<string, Dictionary<int, int>>();
            var totalDrawsPerKey = new Dictionary<string, int>();

            foreach (var row in draws)
            {
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    continue;

                string monthName = date.ToString("MMMM", culture).ToLower();
                int dayOfMonth = date.Day;
                string dayOfWeek = date.ToString("dddd", culture).ToLower();
                var key = $"{monthName}-{dayOfMonth}-{dayOfWeek}";

                if (!dayMonthWeekNumberCounts.TryGetValue(key, out var numberCounts))
                {
                    numberCounts = new Dictionary<int, int>();
                    dayMonthWeekNumberCounts[key] = numberCounts;
                    totalDrawsPerKey[key] = 0;
                }

                foreach (var propName in propNames)
                {
                    var prop = row.GetType().GetProperty(propName);
                    if (prop == null) continue;

                    var val = prop.GetValue(row);
                    if (val == null) continue;

                    if (int.TryParse(val.ToString(), out int number) && number != 0)
                    {
                        if (numberCounts.ContainsKey(number)) numberCounts[number]++;
                        else numberCounts.Add(number, 1);
                        totalDrawsPerKey[key]++;
                    }
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Месяц");
            dt.Columns.Add("День месяца");
            dt.Columns.Add("День недели");
            dt.Columns.Add("Число");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");
            dt.Columns.Add("Тип");

            var sortedKeys = dayMonthWeekNumberCounts.Keys
                .OrderBy(key =>
                {
                    var parts = key.Split('-');
                    int monthNumber = monthOrder.ContainsKey(parts[0]) ? monthOrder[parts[0]] : 0;
                    int day = int.Parse(parts[1]);
                    int dayOfWeekIndex = Array.IndexOf(dayOfWeekNames, parts[2]);
                    return monthNumber * 1000000 + day * 10000 + dayOfWeekIndex;
                }).ToList();

            foreach (var key in sortedKeys)
            {
                var parts = key.Split('-');
                var numberCounts = dayMonthWeekNumberCounts[key];
                int totalDraws = totalDrawsPerKey[key];
                if (totalDraws == 0) continue;

                var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                var frequent = sortedNumbers.Take(10);
                var rare = sortedNumbers.Skip(Math.Max(0, sortedNumbers.Count - 10)).Take(10);

                foreach (var num in frequent)
                {
                    double chance = Math.Round((double)num.Value / totalDraws, 4);
                    dt.Rows.Add(parts[0], parts[1], parts[2], num.Key.ToString(), num.Value.ToString(), chance.ToString(), "Частое");
                }
                foreach (var num in rare)
                {
                    double chance = Math.Round((double)num.Value / totalDraws, 4);
                    dt.Rows.Add(parts[0], parts[1], parts[2], num.Key.ToString(), num.Value.ToString(), chance.ToString(), "Редкое");
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A110_Frequency_By_MonthDayWeek" : title;
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