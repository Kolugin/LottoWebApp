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
    public class A31CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A31CsvExportHelper(LottoDbContext db)
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

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any())
                throw new InvalidOperationException("Не найдены столбцы чисел.");

            // Определяем параметры для игры
            int maxBallNumber, numberOfBalls;
            bool useBonus, hasTime;
            (maxBallNumber, numberOfBalls, useBonus, hasTime) = game?.ToLower() switch
            {
                "keno" => (60, 20, false, false),
                "blitz" => (20, 8, true, true),
                "5x36" => (36, 6, false, false),
                "6x49" => (49, 6, false, false),
                "1224" => (24, 12, false, true),
                _ => (60, 20, false, false)
            };

            // Создаем DataTable
            DataTable dt = new DataTable();
            dt.Columns.Add("Тираж", typeof(int));
            dt.Columns.Add("Дата", typeof(string));
            if (hasTime)
                dt.Columns.Add("Время", typeof(string));

            for (int i = 1; i <= maxBallNumber; i++)
                dt.Columns.Add($"№{i}", typeof(string));

            if (useBonus)
                for (int bonus = 1; bonus <= 4; bonus++)
                    dt.Columns.Add($"Бонус {bonus}", typeof(string));

            // Предзагружаем имена колонок один раз
            var columnNames = new string[maxBallNumber + 1]; // 1-based
            for (int i = 1; i <= maxBallNumber; i++)
                columnNames[i] = $"№{i}";

            foreach (var row in draws)
            {
                var newRow = dt.NewRow();
                var drawProp = first.GetType().GetProperty("Draw");
                var dateProp = first.GetType().GetProperty("Date");
                var timeProp = hasTime ? first.GetType().GetProperty("Time") : null;

                if (drawProp != null)
                    newRow["Тираж"] = drawProp.GetValue(row) ?? 0;
                if (dateProp != null)
                    newRow["Дата"] = dateProp.GetValue(row)?.ToString() ?? "";
                if (hasTime && timeProp != null)
                    newRow["Время"] = timeProp.GetValue(row)?.ToString() ?? "";

                // Заполняем HashSet шаров
                var drawBalls = new HashSet<int>();
                for (int i = 1; i <= numberOfBalls; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                        {
                            drawBalls.Add(num);
                        }
                    }
                }

                // Записываем только выпавшие числа
                for (int ball = 1; ball <= maxBallNumber; ball++)
                {
                    newRow[columnNames[ball]] = drawBalls.Contains(ball) ? ball.ToString() : "*";
                }

                if (useBonus)
                {
                    int bonusBall = 0;
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var bbVal = bbProp.GetValue(row);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int num))
                        {
                            bonusBall = num;
                        }
                    }

                    for (int bonus = 1; bonus <= 4; bonus++)
                    {
                        string col = $"Бонус {bonus}";
                        newRow[col] = (bonusBall == bonus) ? bonus.ToString() : "*";
                    }
                }

                dt.Rows.Add(newRow);
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A31_Universal_Map" : title;
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