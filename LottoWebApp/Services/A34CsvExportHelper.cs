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
    public class A34CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A34CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? differenceType, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            if (string.IsNullOrEmpty(differenceType))
                differenceType = "all";

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
            bool hasBonus, hasTime;
            (maxBallNumber, numberOfBalls, hasBonus, hasTime) = game?.ToLower() switch
            {
                "keno" => (60, 20, false, false),
                "blitz" => (20, 8, true, true),
                "5x36" => (36, 6, false, false),
                "6x49" => (49, 6, false, false),
                "1224" => (24, 12, false, true),
                _ => (60, 20, false, false)
            };

            var resultsTable = new DataTable();
            resultsTable.Columns.Add("Тираж", typeof(int));
            resultsTable.Columns.Add("Дата", typeof(string));
            if (hasTime)
                resultsTable.Columns.Add("Время", typeof(string));

            var positionProps = numberProps.Where(p => p.Name.StartsWith("B") && p.Name != "BB").ToList();
            int posCount = positionProps.Count;

            if (differenceType == "all")
            {
                // Все позиции
                for (int i = 0; i < posCount; i++)
                {
                    for (int j = i + 1; j < posCount; j++)
                    {
                        string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[j].Name.Substring(1)}";
                        resultsTable.Columns.Add(colName, typeof(string));
                    }
                }
            }
            else
            {
                // Соседние позиции
                for (int i = 0; i < posCount - 1; i++)
                {
                    string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[i + 1].Name.Substring(1)}";
                    resultsTable.Columns.Add(colName, typeof(string));
                }
            }

            foreach (var row in draws)
            {
                var newRow = resultsTable.NewRow();
                newRow["Тираж"] = Convert.ToInt32(first.GetType().GetProperty("Draw")?.GetValue(row) ?? 0);
                newRow["Дата"] = first.GetType().GetProperty("Date")?.GetValue(row)?.ToString() ?? "";
                if (hasTime)
                    newRow["Время"] = first.GetType().GetProperty("Time")?.GetValue(row)?.ToString() ?? "";

                if (differenceType == "all")
                {
                    for (int i = 0; i < posCount; i++)
                    {
                        var val1 = positionProps[i].GetValue(row);
                        if (val1 == null || !int.TryParse(val1.ToString(), out int b1) || b1 == 0) continue;

                        for (int j = i + 1; j < posCount; j++)
                        {
                            var val2 = positionProps[j].GetValue(row);
                            int b2 = val2 != null && int.TryParse(val2.ToString(), out int num) && num != 0 ? num : 0;
                            string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[j].Name.Substring(1)}";
                            newRow[colName] = b2 == 0 ? "-" : Math.Abs(b1 - b2).ToString();
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < posCount - 1; i++)
                    {
                        var val1 = positionProps[i].GetValue(row);
                        var val2 = positionProps[i + 1].GetValue(row);

                        if (val1 != null && val2 != null && int.TryParse(val1.ToString(), out int ball1) && int.TryParse(val2.ToString(), out int ball2))
                        {
                            string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[i + 1].Name.Substring(1)}";
                            if (ball1 == 0 || ball2 == 0)
                                newRow[colName] = "-";
                            else
                                newRow[colName] = Math.Abs(ball1 - ball2).ToString();
                        }
                        else
                        {
                            string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[i + 1].Name.Substring(1)}";
                            newRow[colName] = "-";
                        }
                    }
                }

                resultsTable.Rows.Add(newRow);
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A34_Positions_Difference_Map" : title;
            string gameStr = game ?? "unknown";
            string drawCountStr = drawCount?.ToString() ?? "all";

            string safeFileName = $"{titlePart}_{gameStr}_{drawCountStr}_{differenceType}_{DateTime.Now:yyyyMMddHHmmss}.csv"
                .Replace(" ", "_")
                .Replace(":", "_")
                .Replace("/", "_")
                .Replace("\\", "_");

            string filePath = await CsvExportService.ExportToCsvAsync(resultsTable, safeFileName);

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