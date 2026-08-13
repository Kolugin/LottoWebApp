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
    public class A32CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A32CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, bool enableSorting, string? title)
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

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any())
                throw new InvalidOperationException("Не найдены столбцы чисел.");

            // Определяем параметры для игры
            int maxBallNumber, numberOfBalls;
            (maxBallNumber, numberOfBalls) = game?.ToLower() switch
            {
                "keno" => (60, 20),
                "blitz" => (20, 8),
                "5x36" => (36, 6),
                "6x49" => (49, 6),
                "1224" => (24, 12),
                _ => (60, 20)
            };

            // Инициализация массива для подсчёта выпадений по позициям для различных игр
            var totalOccurrencesByPosition = new int[maxBallNumber, numberOfBalls];

            // Перебираем все тиражи
            foreach (var row in draws)
            {
                var currentBalls = new List<int>();

                // Собираем шары текущего тиража, исключая нулевые значения
                for (int i = 1; i <= numberOfBalls; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int ball) && ball > 0)
                        {
                            currentBalls.Add(ball);
                        }
                    }
                }

                // Сортировка чисел тиража, если включена опция сортировки
                if (enableSorting)
                {
                    currentBalls.Sort();
                }

                // Подсчитываем количество выпадений чисел по позициям
                for (int pos = 0; pos < currentBalls.Count; pos++)
                {
                    int currentBall = currentBalls[pos];
                    if (currentBall > 0)
                    {
                        totalOccurrencesByPosition[currentBall - 1, pos]++;
                    }
                }
            }

            // Создаем DataTable
            DataTable dt = new DataTable();
            dt.Columns.Add("Число", typeof(int));

            // Добавляем колонки для каждой позиции
            for (int pos = 1; pos <= numberOfBalls; pos++)
            {
                dt.Columns.Add($"П{pos}", typeof(int));
            }

            // Заполняем таблицу данными из массива totalOccurrencesByPosition
            for (int number = 1; number <= maxBallNumber; number++)
            {
                var newRow = dt.NewRow();
                newRow["Число"] = number;

                for (int pos = 0; pos < numberOfBalls; pos++)
                {
                    newRow[$"П{pos + 1}"] = totalOccurrencesByPosition[number - 1, pos];
                }

                dt.Rows.Add(newRow);
            }

            // Определяем путь сохранения файла в зависимости от состояния сортировки
            string fileNameSuffix = enableSorting ? "Карта_распределения_выпадения_чисел_упорядоченная" : "Карта_распределения_выпадения_чисел";
            string titlePart = string.IsNullOrEmpty(title) ? $"A32_{fileNameSuffix}" : title;
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