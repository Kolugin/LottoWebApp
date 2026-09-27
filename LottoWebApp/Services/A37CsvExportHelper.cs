using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.IO.Compression;

namespace LottoWebApp.Services
{
    public class A37CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A37CsvExportHelper(LottoDbContext db)
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

            bool is320 = game.Equals("320", StringComparison.OrdinalIgnoreCase);
            var propNames320 = new[] { "B11", "B12", "B13", "B21", "B22", "B23" };

            int numberOfBalls = is320 ? 6 : game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            var intervals = new List<int> { 10, 50, 100 };
            var createdFiles = new List<string>();

            string titlePart = string.IsNullOrEmpty(title) ? "A37_Frequency_Distribution_Map" : title;
            string gameStr = game ?? "unknown";
            string drawCountStr = drawCount?.ToString() ?? "all";

            foreach (int interval in intervals)
            {
                var dt = new DataTable();
                dt.Columns.Add("№ Тиражей", typeof(string));
                for (int i = 1; i <= numberOfBalls; i++)
                {
                    dt.Columns.Add($"№Поз {i}", typeof(string));
                }

                var ballCounts = new Dictionary<int, int>();

                for (int startIndex = 0; startIndex <= draws.Count - interval; startIndex++)
                {
                    ballCounts.Clear();
                    var row = dt.NewRow();
                    row["№ Тиражей"] = $"{startIndex + 1}_{startIndex + interval}";

                    for (int i = startIndex; i < startIndex + interval; i++)
                    {
                        var currentRow = draws[i];

                        if (is320)
                        {
                            foreach (var propName in propNames320)
                            {
                                var prop = first.GetType().GetProperty(propName);
                                if (prop != null)
                                {
                                    var val = prop.GetValue(currentRow);
                                    if (val != null && int.TryParse(val.ToString(), out int ball) && ball != 0)
                                    {
                                        if (!ballCounts.ContainsKey(ball))
                                            ballCounts[ball] = 0;
                                        ballCounts[ball]++;
                                    }
                                }
                            }
                        }
                        else
                        {
                            foreach (var prop in numberProps)
                            {
                                if (prop.Name == "BB") continue;
                                var val = prop.GetValue(currentRow);
                                if (val != null && int.TryParse(val.ToString(), out int ball) && ball != 0)
                                {
                                    if (!ballCounts.ContainsKey(ball))
                                        ballCounts[ball] = 0;
                                    ballCounts[ball]++;
                                }
                            }
                        }
                    }

                    var startRow = draws[startIndex];
                    for (int position = 1; position <= numberOfBalls; position++)
                    {
                        int ball = 0;
                        if (is320)
                        {
                            var prop = first.GetType().GetProperty(propNames320[position - 1]);
                            if (prop != null)
                            {
                                var val = prop.GetValue(startRow);
                                if (val != null && int.TryParse(val.ToString(), out int num))
                                    ball = num;
                            }
                        }
                        else
                        {
                            var positionProp = numberProps.FirstOrDefault(p => p.Name == $"B{position}");
                            if (positionProp != null)
                            {
                                var val = positionProp.GetValue(startRow);
                                if (val != null && int.TryParse(val.ToString(), out int num))
                                    ball = num;
                            }
                        }

                        if (ball != 0 && ballCounts.ContainsKey(ball))
                        {
                            double probability = (double)interval / ballCounts[ball];
                            row[$"№Поз {position}"] = probability.ToString("F8", CultureInfo.InvariantCulture);
                        }
                        else
                        {
                            row[$"№Поз {position}"] = "-";
                        }
                    }

                    dt.Rows.Add(row);
                }

                string safeFileName = $"{titlePart}_{gameStr}_{drawCountStr}_{interval}_draws_{DateTime.Now:yyyyMMddHHmmss}.csv"
                    .Replace(" ", "_").Replace(":", "_").Replace("/", "_").Replace("\\", "_");

                string filePath = await CsvExportService.ExportToCsvAsync(dt, safeFileName);
                createdFiles.Add(filePath);
            }

            using var zipMemoryStream = new MemoryStream();
            using (var archive = new ZipArchive(zipMemoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var filePath in createdFiles)
                {
                    string webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    string relativePathWithoutSlash = filePath.TrimStart('/');
                    string fullPath = Path.Combine(webRootPath, relativePathWithoutSlash);

                    if (System.IO.File.Exists(fullPath))
                    {
                        var entry = archive.CreateEntry(Path.GetFileName(fullPath));
                        using var entryStream = entry.Open();
                        using var fileStream = System.IO.File.OpenRead(fullPath);
                        await fileStream.CopyToAsync(entryStream);
                    }
                }
            }

            var zipBytes = zipMemoryStream.ToArray();
            string zipFileName = $"{titlePart}_{gameStr}_{drawCountStr}_all_intervals_{DateTime.Now:yyyyMMddHHmmss}.zip";
            return new FileContentResult(zipBytes, "application/zip") { FileDownloadName = zipFileName };
        }
    }
}