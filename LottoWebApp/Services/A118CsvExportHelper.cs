using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A118CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A118CsvExportHelper(LottoDbContext db)
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
            var dateProp = first.GetType().GetProperty("Date");
            if (dateProp == null)
                throw new InvalidOperationException("Не найдено поле 'Date'.");

            var bonusBallProp = first.GetType().GetProperty("BB");
            if (bonusBallProp == null)
                throw new InvalidOperationException("Не найдено поле 'BB'.");

            var culture = new CultureInfo("ru-RU");
            var monthOrder = new Dictionary<string, int>
                {
                    {"январь", 1},
                    {"февраль", 2},
                    {"март", 3},
                    {"апрель", 4},
                    {"май", 5},
                    {"июнь", 6},
                    {"июль", 7},
                    {"август", 8},
                    {"сентябрь", 9},
                    {"октябрь", 10},
                    {"ноябрь", 11},
                    {"декабрь", 12}
                };

            var dayOfWeekOrder = new List<string> { "понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье" };
            var dayMonthWeekNumberCounts = new Dictionary<string, Dictionary<int, int>>();
            var totalDrawsPerKey = new Dictionary<string, int>();

            // Обработка данных из таблицы
            foreach (var row in draws)
            {
                // Получаем дату
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue; // Пропускаем ошибочные даты
                }

                string monthName = date.ToString("MMMM", culture).ToLower();
                int dayOfMonth = date.Day;
                string dayOfWeek = date.ToString("dddd", culture).ToLower();
                var key = $"{monthName}-{dayOfMonth}-{dayOfWeek}";

                // Инициализация словарей только при необходимости
                if (!dayMonthWeekNumberCounts.TryGetValue(key, out var numberCounts))
                {
                    numberCounts = new Dictionary<int, int>();
                    dayMonthWeekNumberCounts[key] = numberCounts;
                }

                // Получаем значение BB
                var bbValue = bonusBallProp.GetValue(row);
                if (bbValue == null || !int.TryParse(bbValue.ToString(), out int number) || number == 0) continue;

                // Обновление счетчиков
                if (numberCounts.TryGetValue(number, out int count))
                {
                    numberCounts[number] = count + 1;
                }
                else
                {
                    numberCounts[number] = 1;
                }

                // Обновление общего количества тиражей для ключа
                if (totalDrawsPerKey.TryGetValue(key, out int totalDraws))
                {
                    totalDrawsPerKey[key] = totalDraws + 1;
                }
                else
                {
                    totalDrawsPerKey[key] = 1;
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Месяц");
            dt.Columns.Add("День месяца");
            dt.Columns.Add("День недели");
            dt.Columns.Add("Число");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");

            // Формирование результатов
            var sortedKeys = dayMonthWeekNumberCounts.Keys
                .OrderBy(key =>
                {
                    var parts = key.Split('-');
                    int monthNumber = monthOrder[parts[0]];
                    int dayOfMonth = int.Parse(parts[1]);
                    int dayOfWeekIndex = dayOfWeekOrder.IndexOf(parts[2]);
                    return monthNumber * 10000 + dayOfMonth * 100 + dayOfWeekIndex;
                })
                .ToList();

            foreach (var key in sortedKeys)
            {
                var parts = key.Split('-');
                string monthName = parts[0];
                int dayOfMonth = int.Parse(parts[1]);
                string dayOfWeek = parts[2];
                var numberCounts = dayMonthWeekNumberCounts[key];
                int totalDraws = totalDrawsPerKey[key];

                if (totalDraws == 0) continue; // Пропускаем пустые группы

                var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                var frequentNumbers = sortedNumbers.Take(10); // В оригинальном алгоритме: первые 10 частых
                foreach (var num in frequentNumbers)
                {
                    double chance = Math.Round((double)num.Value / totalDraws * 100, 4);
                    dt.Rows.Add(monthName, dayOfMonth, dayOfWeek, num.Key.ToString(), num.Value.ToString(), chance.ToString());
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A118_Bonus_Ball_By_MonthDayWeek_Frequency" : title;
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