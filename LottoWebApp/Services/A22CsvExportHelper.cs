using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LottoWebApp.Services
{
    public class A22CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A22CsvExportHelper(LottoDbContext db)
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

            var allDraws = draws
                .Select(row =>
                {
                    var numbers = new List<int>();
                    foreach (var propName in propNames)
                    {
                        var prop = row.GetType().GetProperty(propName);
                        if (prop == null) return numbers;
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num != 0)
                        {
                            numbers.Add(num);
                        }
                    }
                    return numbers;
                })
                .ToList();

            int totalRows = allDraws.Count;

            var ballCounts = new Dictionary<int, int>();
            var freq10 = new Dictionary<int, FrequencyTracker>();
            var freq50 = new Dictionary<int, FrequencyTracker>();
            var freq100 = new Dictionary<int, FrequencyTracker>();

            try
            {
                foreach (var draw in allDraws)
                {
                    foreach (var number in draw)
                    {
                        if (!ballCounts.ContainsKey(number))
                            ballCounts[number] = 0;
                        ballCounts[number]++;
                    }
                }

                ProcessWindows(allDraws, 10, freq10);
                ProcessWindows(allDraws, 50, freq50);
                ProcessWindows(allDraws, 100, freq100);

                DataTable dt = new DataTable();
                dt.Columns.Add("№ Шара");
                dt.Columns.Add("Частота общая");
                dt.Columns.Add("Частота на 10 последних тиражей");
                dt.Columns.Add("Частота на 50 последних тиражей");
                dt.Columns.Add("Частота на 100 последних тиражей");

                foreach (var ball in ballCounts.Keys.OrderBy(k => k))
                {
                    double totalFreq = Math.Round((double)ballCounts[ball] / totalRows, 9);
                    double most10 = freq10.TryGetValue(ball, out var t10) ? Math.Round(t10.GetMostFrequent(), 9) : 0;
                    double most50 = freq50.TryGetValue(ball, out var t50) ? Math.Round(t50.GetMostFrequent(), 9) : 0;
                    double most100 = freq100.TryGetValue(ball, out var t100) ? Math.Round(t100.GetMostFrequent(), 9) : 0;

                    dt.Rows.Add(ball.ToString(), totalFreq.ToString(), most10.ToString(), most50.ToString(), most100.ToString());
                }

                string titlePart = string.IsNullOrEmpty(title) ? "A22_Frequency_Most_Frequent" : title;
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
            finally
            {
                ballCounts.Clear();
                freq10.Clear();
                freq50.Clear();
                freq100.Clear();
                allDraws.Clear();
            }
        }

        private void ProcessWindows(List<List<int>> allDraws, int windowSize, Dictionary<int, FrequencyTracker> frequencyTracker)
        {
            int totalDraws = allDraws.Count;
            if (totalDraws < windowSize) return;

            Span<int> counts = stackalloc int[100];
            HashSet<int> activeNumbers = new HashSet<int>();

            for (int i = 0; i < windowSize; i++)
            {
                foreach (int num in allDraws[i])
                {
                    if (counts[num]++ == 0)
                        activeNumbers.Add(num);
                }
            }

            foreach (int num in activeNumbers)
            {
                if (!frequencyTracker.TryGetValue(num, out var tracker))
                    frequencyTracker[num] = tracker = new FrequencyTracker();

                double freq = (double)windowSize / counts[num];
                tracker.Add(freq);
            }

            for (int i = windowSize; i < totalDraws; i++)
            {
                foreach (int num in allDraws[i - windowSize])
                {
                    if (--counts[num] == 0)
                        activeNumbers.Remove(num);
                }

                foreach (int num in allDraws[i])
                {
                    if (counts[num]++ == 0)
                        activeNumbers.Add(num);
                }

                foreach (int num in activeNumbers)
                {
                    if (!frequencyTracker.TryGetValue(num, out var tracker))
                        frequencyTracker[num] = tracker = new FrequencyTracker();

                    double freq = (double)windowSize / counts[num];
                    tracker.Add(freq);
                }
            }
        }

        public class FrequencyTracker
        {
            private readonly Dictionary<double, int> frequencyMap = new();
            private int totalCount = 0;
            private double mostFrequentValue = 0;
            private int maxCount = 0;
            private readonly int precision;

            public FrequencyTracker(int decimalPlaces = 8)
            {
                precision = decimalPlaces;
            }

            public void Add(double value)
            {
                double rounded = Math.Round(value, precision);

                if (!frequencyMap.TryGetValue(rounded, out int count))
                    count = 0;

                frequencyMap[rounded] = ++count;

                if (count > maxCount || (count == maxCount && rounded < mostFrequentValue))
                {
                    mostFrequentValue = rounded;
                    maxCount = count;
                }

                totalCount++;
            }

            public double GetMostFrequent()
            {
                return totalCount == 0 ? 0 : mostFrequentValue;
            }
        }
    }
}