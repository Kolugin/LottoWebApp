using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LottoWebApp.Services
{
    public class A21CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A21CsvExportHelper(LottoDbContext db)
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
                    .Where(p =>
                    {
                        if (p.Name == "BB" && game?.ToLower() == "blitz")
                            return false;
                        return p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _);
                    })
                    .OrderBy(p => int.Parse(p.Name.Substring(1)))
                    .Select(p => p.Name)
                    .ToArray();

            if (!propNames.Any())
                throw new InvalidOperationException("Не найдены столбцы чисел.");

            var ballCounts = new Dictionary<int, int>();
            var last10Counts = new Dictionary<int, int>();
            var last50Counts = new Dictionary<int, int>();
            var last100Counts = new Dictionary<int, int>();

            int totalRows = draws.Count;

            for (int drawIndex = 0; drawIndex < totalRows; drawIndex++)
            {
                var currentRow = draws[drawIndex];
                var currentNumbers = new List<int>();

                foreach (var propName in propNames)
                {
                    var prop = currentRow.GetType().GetProperty(propName);
                    if (prop == null) continue;

                    var val = prop.GetValue(currentRow);
                    if (val != null && int.TryParse(val.ToString(), out int num) && num != 0)
                    {
                        currentNumbers.Add(num);
                    }
                }

                foreach (var number in currentNumbers)
                {
                    if (ballCounts.ContainsKey(number))
                        ballCounts[number]++;
                    else
                        ballCounts[number] = 1;

                    if (drawIndex < 10) UpdateCounter(last10Counts, number);
                    if (drawIndex < 50) UpdateCounter(last50Counts, number);
                    if (drawIndex < 100) UpdateCounter(last100Counts, number);
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("№ Шара");
            dt.Columns.Add("Частота общая");
            dt.Columns.Add("Частота на 10 последних тиражей");
            dt.Columns.Add("Частота на 50 последних тиражей");
            dt.Columns.Add("Частота на 100 последних тиражей");

            int maxNumber = is320 ? 20 : ballCounts.Keys.Concat(last10Counts.Keys).Concat(last50Counts.Keys).Concat(last100Counts.Keys).DefaultIfEmpty(0).Max();

            for (int ball = 1; ball <= maxNumber; ball++)
            {
                int totalCount = ballCounts.TryGetValue(ball, out int cTotal) ? cTotal : 0;
                int count10 = last10Counts.TryGetValue(ball, out int c10) ? c10 : 0;
                int count50 = last50Counts.TryGetValue(ball, out int c50) ? c50 : 0;
                int count100 = last100Counts.TryGetValue(ball, out int c100) ? c100 : 0;

                if (totalCount == 0 && count10 == 0 && count50 == 0 && count100 == 0) continue;

                double totalFreq = totalCount > 0 ? Math.Round((double)totalRows / totalCount, 9) : 0;
                double freq10 = count10 > 0 ? Math.Round(10.0 / count10, 9) : 0;
                double freq50 = count50 > 0 ? Math.Round(50.0 / count50, 9) : 0;
                double freq100 = count100 > 0 ? Math.Round(100.0 / count100, 9) : 0;

                dt.Rows.Add(ball.ToString(), totalFreq.ToString(), freq10.ToString(), freq50.ToString(), freq100.ToString());
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A21_Frequency_10_50_100" : title;
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

        private void UpdateCounter(Dictionary<int, int> counter, int number)
        {
            if (counter.ContainsKey(number))
                counter[number]++;
            else
                counter[number] = 1;
        }
    }
}