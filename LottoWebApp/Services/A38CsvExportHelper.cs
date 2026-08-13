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
    public class A38CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A38CsvExportHelper(LottoDbContext db)
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
            int maxBallNumber = game?.ToLower() switch
            {
                "keno" => 60,
                "blitz" => 20,
                "5x36" => 36,
                "6x49" => 49,
                "1224" => 24,
                _ => 60
            };

            int ballsPerDraw = game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            // Определяем размеры матрицы
            (int rows, int cols) = game?.ToLower() switch
            {
                "keno" => (6, 10), // 60 = 6 * 10
                "blitz" => (4, 5), // 20 = 4 * 5
                "5x36" => (6, 6), // 36 = 6 * 6
                "6x49" => (7, 7), // 49 = 7 * 7
                "1224" => (4, 6), // 24 = 4 * 6
                _ => (6, 10)
            };

            bool hasTime = game?.ToLower() == "blitz" || game?.ToLower() == "1224";

            DataTable dt = new DataTable();
            dt.Columns.Add("№ Тиража");
            dt.Columns.Add("Дата");
            if (hasTime)
                dt.Columns.Add("Время");

            for (int i = 1; i <= cols; i++)
                dt.Columns.Add($"№{i}");

            foreach (var row in draws)
            {
                // Создаем пустую матрицу
                int[,] matrix = new int[rows, cols];
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                        matrix[r, c] = -1;

                // Заполняем номера шаров
                for (int i = 1; i <= ballsPerDraw; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int ball) && ball >= 1 && ball <= rows * cols)
                        {
                            int r = (ball - 1) / cols;
                            int c = (ball - 1) % cols;
                            matrix[r, c] = ball;
                        }
                    }
                }

                // Формируем строки
                for (int r = 0; r < rows; r++)
                {
                    var newRow = dt.NewRow();
                    int colIndex = 0;

                    if (r == 0)
                    {
                        var drawProp = first.GetType().GetProperty("Draw");
                        var dateProp = first.GetType().GetProperty("Date");
                        var timeProp = hasTime ? first.GetType().GetProperty("Time") : null;

                        newRow[colIndex++] = drawProp?.GetValue(row)?.ToString() ?? "";
                        newRow[colIndex++] = dateProp?.GetValue(row)?.ToString() ?? "";
                        if (hasTime && timeProp != null)
                            newRow[colIndex++] = timeProp.GetValue(row)?.ToString() ?? "";
                    }
                    else
                    {
                        colIndex += 2;
                        if (hasTime)
                            colIndex += 1;
                    }

                    // Заполняем ряд матрицы
                    for (int c = 0; c < cols; c++)
                    {
                        int val = matrix[r, c];
                        newRow[colIndex++] = val == -1 ? "*" : val.ToString();
                    }

                    dt.Rows.Add(newRow);
                }

                // Пустая строка-разделитель
                dt.Rows.Add(dt.NewRow());
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A38_Ticket_Number_Map" : title;
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