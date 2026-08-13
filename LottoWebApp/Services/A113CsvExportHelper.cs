using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Services
{
    public class A113CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A113CsvExportHelper(LottoDbContext db)
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

            // Кэшируем культуру и маппинг дней недели
            var russianCulture = new CultureInfo("ru-RU");
            var dayOfWeekNumberCounts = new Dictionary<string, Dictionary<int, int>>();

            foreach (var row in draws)
            {
                // Получаем дату
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                // Парсим дату из строки
                if (!DateTime.TryParse(dateValue.ToString(), out DateTime date))
                    continue;

                // Получаем название дня недели
                string dayOfWeek = date.ToString("dddd", russianCulture).ToLower();

                // Пропускаем некорректные значения
                var bbValue = bonusBallProp.GetValue(row);
                if (bbValue == null || !int.TryParse(bbValue.ToString(), out int number) || number == 0)
                    continue;

                // Инициализация словаря для дня недели
                if (!dayOfWeekNumberCounts.TryGetValue(dayOfWeek, out var numberCounts))
                {
                    numberCounts = new Dictionary<int, int>();
                    dayOfWeekNumberCounts[dayOfWeek] = numberCounts;
                }

                // Обновление счетчиков (оптимизированная версия)
                numberCounts[number] = numberCounts.TryGetValue(number, out int count) ? count + 1 : 1;
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("День недели");
            dt.Columns.Add("№ Шара");
            dt.Columns.Add("Кол-во выпадений");
            dt.Columns.Add("Шанс (%)");

            // Формирование результатов
            foreach (var dayEntry in dayOfWeekNumberCounts)
            {
                string currentDayOfWeek = dayEntry.Key;
                var numberCounts = dayEntry.Value;
                int totalDraws = numberCounts.Values.Sum();

                foreach (var numEntry in numberCounts)
                {
                    double chance = Math.Round((double)numEntry.Value / totalDraws * 100, 4);
                    dt.Rows.Add(currentDayOfWeek, numEntry.Key.ToString(), numEntry.Value.ToString(), chance.ToString());
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A113_Bonus_Ball_By_Weekday_Frequency" : title;
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