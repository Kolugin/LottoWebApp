using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Text;

namespace LottoWebApp.Services
{
    public class A41CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A41CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string game, int selectedNumber, int? drawCount, string? direction)
        {
            var query = new LotteryQueryProvider(_db).GetQuery(game);

            bool isDesc = direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (drawCount.HasValue && drawCount.Value > 0)
                query = query.Take(drawCount.Value);

            var draws = await query.ToListAsync();
            if (!draws.Any()) throw new InvalidOperationException("Нет данных для экспорта.");

            var first = draws.First();

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any()) throw new InvalidOperationException("Не найдены столбцы чисел.");

            // Определяем количество шаров в зависимости от игры
            int ballCount = 0;
            switch (game?.ToLower())
            {
                case "keno":
                    ballCount = 20;
                    break;
                case "blitz":
                    ballCount = 8;
                    break;
                case "5x36":
                    ballCount = 6;
                    break;
                case "6x49":
                    ballCount = 6;
                    break;
                case "1224":
                    ballCount = 12;
                    break;
                default:
                    ballCount = 20;
                    break;
            }

            int totalDraws = draws.Count;
            int occurrences = 0;
            string? lastOccurrenceDate = null;

            var dateList = new List<string>();
            var timeList = new List<string>();

            foreach (var rowObj in draws)
            {
                var rowType = rowObj.GetType();
                var dateProp = rowType.GetProperty("Date");
                var timeProp = (game?.ToLower() == "blitz" || game?.ToLower() == "1224") ? rowType.GetProperty("Time") : null;

                var drawBalls = new List<int>();
                for (int i = 1; i <= ballCount; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(rowObj);
                        if (val != null && int.TryParse(val.ToString(), out int num))
                        {
                            drawBalls.Add(num);
                        }
                    }
                }

                if (drawBalls.Contains(selectedNumber))
                {
                    occurrences++;
                    var dateValue = dateProp?.GetValue(rowObj)?.ToString();
                    var timeValue = timeProp?.GetValue(rowObj)?.ToString();

                    if (dateValue != null)
                    {
                        lastOccurrenceDate = dateValue;
                        dateList.Add(dateValue);
                        timeList.Add(timeValue ?? "");
                    }
                }
            }

            double probabilityOverall = (occurrences / (double)totalDraws) * 100;

            var csvContent = new StringBuilder();
            csvContent.AppendLine($"Статистика для шара №{selectedNumber}");
            csvContent.AppendLine($"Выпал {occurrences} раз(а)");
            csvContent.AppendLine($"Последний раз выпал: {(lastOccurrenceDate ?? "нет данных")}");
            csvContent.AppendLine($"Общий шанс выпадения в %: {probabilityOverall:F4}%");

            // Заголовок
            if (game?.ToLower() == "blitz" || game?.ToLower() == "1224")
                csvContent.AppendLine("Дата;Время");
            else
                csvContent.AppendLine("Дата");

            // Заполняем строки
            for (int i = 0; i < dateList.Count; i++)
            {
                if (game?.ToLower() == "blitz" || game?.ToLower() == "1224")
                {
                    string time = (i < timeList.Count) ? timeList[i] : "";
                    csvContent.AppendLine($"{dateList[i]};{time}");
                }
                else
                {
                    csvContent.AppendLine(dateList[i]);
                }
            }

            string fileName = $"4.1_{game.ToUpper()}_Статистика_шара_{selectedNumber}_{DateTime.Now:yyyyMMddHHmmss}.csv";
            string filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", fileName);
            await File.WriteAllTextAsync(filePath, csvContent.ToString(), Encoding.UTF8);

            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return new FileContentResult(bytes, "text/csv") { FileDownloadName = fileName };
        }
    }
}