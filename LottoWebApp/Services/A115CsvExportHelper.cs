using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A115CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A115CsvExportHelper(LottoDbContext db)
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

            var dayOfMonthNumberCounts = new Dictionary<int, Dictionary<int, int>>();
            var dayOfMonthTotalDraws = new Dictionary<int, int>();

            foreach (var row in draws)
            {
                // Получаем дату
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue; // Пропускаем ошибочные даты
                }

                int dayOfMonth = date.Day;

                // Инициализация словарей только при необходимости
                if (!dayOfMonthNumberCounts.TryGetValue(dayOfMonth, out var numberCounts))
                {
                    numberCounts = new Dictionary<int, int>();
                    dayOfMonthNumberCounts[dayOfMonth] = numberCounts;
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

                // Обновление общего количества тиражей для дня месяца
                if (dayOfMonthTotalDraws.TryGetValue(dayOfMonth, out int totalDraws))
                {
                    dayOfMonthTotalDraws[dayOfMonth] = totalDraws + 1;
                }
                else
                {
                    dayOfMonthTotalDraws[dayOfMonth] = 1;
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("День месяца");
            dt.Columns.Add("№ Шара");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");

            // Формирование результатов
            for (int day = 1; day <= 31; day++)
            {
                if (dayOfMonthNumberCounts.TryGetValue(day, out var numberCounts))
                {
                    int totalDraws = dayOfMonthTotalDraws[day];
                    if (totalDraws == 0) continue; // Пропускаем пустые группы

                    var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                    var frequentNumbers = sortedNumbers.Take(4); // В оригинальном алгоритме: первые 4 частых
                    foreach (var num in frequentNumbers)
                    {
                        double chance = Math.Round((double)num.Value / totalDraws * 100, 4);
                        dt.Rows.Add(day, num.Key.ToString(), num.Value.ToString(), chance.ToString());
                    }
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A115_Bonus_Ball_By_DayOfMonth_Frequency" : title;
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