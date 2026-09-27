using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LottoWebApp.Services
{
    public class A23CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A23CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? title, string? selectedTable = null)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            var query = new LotteryQueryProvider(_db).GetQuery(game);
            query = query.OrderBy(e => EF.Property<int>(e, "Draw"));

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

            int totalBalls = 0;
            int ballCount = 0;
            bool hasTime = false;
            bool hasBonus = false;

            if (is320)
            {
                totalBalls = 20;
                ballCount = 3;
                hasTime = true;
                hasBonus = false;
            }
            else
            {
                switch (game?.ToLower())
                {
                    case "keno":
                        totalBalls = 60;
                        ballCount = 20;
                        hasTime = false;
                        hasBonus = false;
                        break;
                    case "blitz":
                        totalBalls = 20;
                        ballCount = 8;
                        hasTime = true;
                        hasBonus = true;
                        break;
                    case "5x36":
                        totalBalls = 36;
                        ballCount = 6;
                        hasTime = false;
                        hasBonus = false;
                        break;
                    case "6x49":
                        totalBalls = 49;
                        ballCount = 6;
                        hasTime = false;
                        hasBonus = false;
                        break;
                    case "1224":
                        totalBalls = 24;
                        ballCount = 12;
                        hasTime = true;
                        hasBonus = false;
                        break;
                    default:
                        totalBalls = 60;
                        ballCount = 20;
                        hasTime = false;
                        hasBonus = false;
                        break;
                }
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Тираж");
            dt.Columns.Add("Дата");
            if (hasTime)
            {
                dt.Columns.Add("Время");
            }
            for (int i = 1; i <= totalBalls; i++)
            {
                dt.Columns.Add($"№{i}");
            }

            if (hasBonus)
            {
                for (int b = 1; b <= 4; b++)
                {
                    dt.Columns.Add($"Бонус {b}");
                }
            }

            int[] pauseCounters = new int[totalBalls + 1];
            int[] bonusPauseCounters = new int[5];

            var tempRows = new List<object[]>();

            foreach (var rowObj in draws)
            {
                var rowType = rowObj.GetType();
                var drawProp = rowType.GetProperty("Draw");
                var dateProp = rowType.GetProperty("Date");
                var timeProp = hasTime ? rowType.GetProperty("Time") : null;

                if (drawProp == null || dateProp == null) continue;

                var drawValue = drawProp.GetValue(rowObj);
                var dateValue = dateProp.GetValue(rowObj);
                var timeValue = hasTime && timeProp != null ? timeProp.GetValue(rowObj) : null;

                var drawnBalls = new HashSet<int>();

                if (is320)
                {
                    var propNames = new[] { $"{exportField}1", $"{exportField}2", $"{exportField}3" };
                    foreach (var propName in propNames)
                    {
                        var prop = rowType.GetProperty(propName);
                        if (prop != null)
                        {
                            var val = prop.GetValue(rowObj);
                            if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                            {
                                drawnBalls.Add(num);
                            }
                        }
                    }
                }
                else
                {
                    var numberProps = first.GetType().GetProperties()
                        .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                        .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                        .ToList();

                    for (int i = 1; i <= ballCount; i++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(rowObj);
                            if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                            {
                                drawnBalls.Add(num);
                            }
                        }
                    }
                }

                int bonusBall = 0;
                if (hasBonus)
                {
                    var bbProp = rowType.GetProperty("BB");
                    if (bbProp != null)
                    {
                        var val = bbProp.GetValue(rowObj);
                        if (val != null && int.TryParse(val.ToString(), out int num))
                        {
                            bonusBall = num;
                        }
                    }
                }

                var newRow = new object[dt.Columns.Count];
                newRow[0] = drawValue?.ToString() ?? "";
                newRow[1] = dateValue?.ToString() ?? "";
                int colIndex = 2;
                if (hasTime)
                {
                    newRow[colIndex] = timeValue?.ToString() ?? "";
                    colIndex++;
                }

                for (int ball = 1; ball <= totalBalls; ball++)
                {
                    if (drawnBalls.Contains(ball))
                        pauseCounters[ball] = 0;
                    else
                        pauseCounters[ball]++;

                    newRow[colIndex] = pauseCounters[ball].ToString();
                    colIndex++;
                }

                if (hasBonus)
                {
                    for (int b = 1; b <= 4; b++)
                    {
                        if (bonusBall == b)
                            bonusPauseCounters[b] = 0;
                        else
                            bonusPauseCounters[b]++;

                        newRow[colIndex] = bonusPauseCounters[b].ToString();
                        colIndex++;
                    }
                }

                tempRows.Add(newRow);
            }

            bool isDesc = direction == "newToOld";
            if (isDesc)
            {
                foreach (var row in tempRows.AsEnumerable().Reverse())
                {
                    dt.Rows.Add(row);
                }
            }
            else
            {
                foreach (var row in tempRows)
                {
                    dt.Rows.Add(row);
                }
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A23_Ball_Pause_Map" : title;
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
    }
}