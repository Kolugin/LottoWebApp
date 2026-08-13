using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LottoWebApp.Services
{
    public class A14CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A14CsvExportHelper(LottoDbContext db)
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

            // --- ЛОГИКА ДЛЯ 320 ---
            if (game.Equals("320", StringComparison.OrdinalIgnoreCase))
            {
                string exportTable = selectedTable?.ToLowerInvariant() switch
                {
                    "b1" => "B1",
                    "b2" => "B2",
                    _ => "B1" // по умолчанию
                };

                Dictionary<int, int> counts = new();
                int total = 0;

                foreach (var item in draws)
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        string propName = exportTable == "B1" ? $"B1{i}" : $"B2{i}";
                        var prop = item.GetType().GetProperty(propName);
                        if (prop != null)
                        {
                            var val = prop.GetValue(item);
                            if (int.TryParse(val?.ToString(), out var num) && num > 0)
                            {
                                if (!counts.ContainsKey(num))
                                    counts[num] = 1;
                                else
                                    counts[num]++;
                                total++;
                            }
                        }
                    }
                }

                if (total == 0)
                    throw new InvalidOperationException($"Не удалось вычислить частоты для {exportTable}.");

                var sorted = counts.OrderByDescending(v => v.Value).ToList();
                int take = Math.Min(10, sorted.Count);

                DataTable dt = new DataTable();
                dt.Columns.Add("Тип");               // Частое / Редкое
                dt.Columns.Add("Число");
                dt.Columns.Add("Кол-во выпадений");
                dt.Columns.Add("Вероятность (%)");

                // Частые
                foreach (var item in sorted.Take(take))
                    dt.Rows.Add("Частое", item.Key.ToString(), item.Value.ToString(), Math.Round(item.Value / (double)total * 100, 4).ToString());

                // Редкие
                int start = Math.Max(0, sorted.Count - take);
                foreach (var item in sorted.Skip(start).Take(take).OrderBy(v => v.Value))
                    dt.Rows.Add("Редкое", item.Key.ToString(), item.Value.ToString(), Math.Round(item.Value / (double)total * 100, 4).ToString());

                string titlePart = string.IsNullOrEmpty(title) ? "A14_Frequency_Analysis" : title;
                string drawCountStr = drawCount?.ToString() ?? "all";

                string safeFileName = $"{titlePart}_{game}_{exportTable}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
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
            // --- ОСТАЛЬНЫЕ ИГРЫ ---
            else
            {
                var first = draws.First();

                var numberProps = first.GetType().GetProperties()
                    .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                    .OrderBy(p => int.Parse(p.Name.Substring(1)))
                    .ToList();

                Dictionary<int, int> counts = new();
                int total = 0;

                foreach (var item in draws)
                {
                    foreach (var p in numberProps)
                    {
                        var val = p.GetValue(item);
                        if (val == null) continue;

                        if (!int.TryParse(val.ToString(), out int num) || num == 0)
                            continue;

                        if (game.Equals("5x36", StringComparison.OrdinalIgnoreCase) && num == 0)
                            continue;

                        if (!counts.ContainsKey(num))
                            counts[num] = 1;
                        else
                            counts[num]++;

                        total++;
                    }
                }

                if (total == 0)
                    throw new InvalidOperationException("Не удалось вычислить частоты.");

                var sorted = counts.OrderByDescending(v => v.Value).ToList();
                int take = Math.Min(10, sorted.Count);

                DataTable dt = new DataTable();
                dt.Columns.Add("Тип");               // Частое / Редкое
                dt.Columns.Add("Число");
                dt.Columns.Add("Кол-во выпадений");
                dt.Columns.Add("Вероятность (%)");

                // Частые
                foreach (var item in sorted.Take(take))
                    dt.Rows.Add("Частое", item.Key.ToString(), item.Value.ToString(), Math.Round(item.Value / (double)total * 100, 4).ToString());

                // Редкие
                int start = Math.Max(0, sorted.Count - take);
                foreach (var item in sorted.Skip(start).Take(take).OrderBy(v => v.Value))
                    dt.Rows.Add("Редкое", item.Key.ToString(), item.Value.ToString(), Math.Round(item.Value / (double)total * 100, 4).ToString());

                string titlePart = string.IsNullOrEmpty(title) ? "A14_Frequency_Analysis" : title;
                string drawCountStr = drawCount?.ToString() ?? "all";

                string safeFileName = $"{titlePart}_{game}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
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
}