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
    public class A33CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A33CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, int combinationSize, bool includeBonus, string? title)
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
            bool canIncludeBonus;
            (maxBallNumber, numberOfBalls, canIncludeBonus) = game?.ToLower() switch
            {
                "keno" => (60, 20, false),
                "blitz" => (20, 8, true),
                "5x36" => (36, 6, false),
                "6x49" => (49, 6, false),
                "1224" => (24, 12, false),
                _ => (60, 20, false)
            };

            if (includeBonus && !canIncludeBonus)
                throw new InvalidOperationException("Для этой игры бонус недоступен.");

            var combinations = GenerateCombinations(combinationSize, 1, maxBallNumber).ToList();
            var parsedCombinations = combinations
                .Select(c => c.Split('_').Select(int.Parse).ToArray())
                .ToArray(); // индексируем по позиции

            var matchedMap = new Dictionary<int, HashSet<int>>(); // drawID -> set of matched indexes

            var drawBallsMap = new Dictionary<int, HashSet<int>>();
            var bonusBallMap = new Dictionary<int, int>();

            foreach (var row in draws)
            {
                int drawNumber = Convert.ToInt32(first.GetType().GetProperty("Draw")?.GetValue(row) ?? 0);

                var drawBalls = new HashSet<int>();
                for (int i = 1; i <= numberOfBalls; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int ball) && ball > 0)
                        {
                            drawBalls.Add(ball);
                        }
                    }
                }

                drawBallsMap[drawNumber] = drawBalls;

                if (includeBonus)
                {
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var bbVal = bbProp.GetValue(row);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int bonusBall))
                        {
                            bonusBallMap[drawNumber] = bonusBall;
                        }
                    }
                }
            }

            foreach (var drawEntry in drawBallsMap)
            {
                int drawNumber = drawEntry.Key;
                var drawBalls = drawEntry.Value;

                var matchedIndexes = new HashSet<int>();

                if (includeBonus && bonusBallMap.TryGetValue(drawNumber, out int bonusBall))
                {
                    for (int i = 0; i < parsedCombinations.Length; i++)
                    {
                        if (parsedCombinations[i].All(drawBalls.Contains) && bonusBall >= 1 && bonusBall <= 4)
                            matchedIndexes.Add(i);
                    }
                }
                else
                {
                    for (int i = 0; i < parsedCombinations.Length; i++)
                    {
                        if (parsedCombinations[i].All(drawBalls.Contains))
                            matchedIndexes.Add(i);
                    }
                }

                matchedMap[drawNumber] = matchedIndexes;
            }

            // Экспорт результатов в CSV
            string baseFileName = $"3.3_{game.ToUpper()}_Комбинация_{combinationSize}_чисел{(includeBonus ? "_+1Б" : "")}";
            string extension = ".csv";
            string fullBasePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", baseFileName);

            int maxRowsPerFile = 500000;
            int maxColumnsPerFile = 15000;

            var baseHeaders = new List<string> { "Тираж", "Дата" };
            if (game?.ToLower() == "blitz" || game?.ToLower() == "1224")
                baseHeaders.Add("Время");

            int totalCombinations = combinations.Count;
            var sortedDraws = draws.OrderBy(d => Convert.ToInt32(d.GetType().GetProperty("Draw")?.GetValue(d) ?? 0)).ToList();
            int totalRows = sortedDraws.Count;

            int columnBlockIndex = 1;
            var createdFiles = new List<string>();

            for (int comboStart = 0; comboStart < totalCombinations; comboStart += maxColumnsPerFile)
            {
                int comboEnd = Math.Min(comboStart + maxColumnsPerFile, totalCombinations);
                var currentComboBlock = combinations.GetRange(comboStart, comboEnd - comboStart);

                int rowBlockIndex = 1;
                int rowCounter = 0;
                string currentFilePath = $"{fullBasePath}_{columnBlockIndex}_{rowBlockIndex}{extension}";
                var writer = new System.IO.StreamWriter(currentFilePath, false, Encoding.UTF8);

                try
                {
                    var header = new List<string>(baseHeaders);
                    header.AddRange(currentComboBlock);
                    await writer.WriteLineAsync(string.Join(";", header));

                    foreach (var row in sortedDraws)
                    {
                        if (rowCounter >= maxRowsPerFile)
                        {
                            await writer.FlushAsync();
                            await writer.DisposeAsync();

                            // Добавляем файл в список перед созданием нового
                            createdFiles.Add(currentFilePath);

                            rowBlockIndex++;
                            rowCounter = 0;

                            currentFilePath = $"{fullBasePath}_{columnBlockIndex}_{rowBlockIndex}{extension}";
                            writer = new System.IO.StreamWriter(currentFilePath, false, Encoding.UTF8);

                            await writer.WriteLineAsync(string.Join(";", header));
                        }

                        int draw = Convert.ToInt32(first.GetType().GetProperty("Draw")?.GetValue(row) ?? 0);
                        var dateProp = first.GetType().GetProperty("Date");
                        string date = dateProp?.GetValue(row)?.ToString() ?? "";

                        var timeProp = (game?.ToLower() == "blitz" || game?.ToLower() == "1224") ? first.GetType().GetProperty("Time") : null;
                        string time = timeProp?.GetValue(row)?.ToString() ?? "";

                        var line = new List<string> { draw.ToString(), date };
                        if (!string.IsNullOrEmpty(time))
                            line.Add(time);

                        var matched = matchedMap.ContainsKey(draw) ? matchedMap[draw] : new HashSet<int>();
                        for (int i = comboStart; i < comboEnd; i++)
                        {
                            line.Add(matched.Contains(i) ? "1" : "0");
                        }

                        await writer.WriteLineAsync(string.Join(";", line));
                        rowCounter++;
                    }
                }
                finally
                {
                    await writer.DisposeAsync();
                }

                // Добавляем последний файл в блоке колонок
                createdFiles.Add(currentFilePath);
                columnBlockIndex++;
            }

            // Возвращаем архив со всеми файлами
            using var zipMemoryStream = new MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(zipMemoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                foreach (var filePath in createdFiles)
                {
                    var entry = archive.CreateEntry(Path.GetFileName(filePath));
                    using var entryStream = entry.Open();
                    using var fileStream = System.IO.File.OpenRead(filePath);
                    await fileStream.CopyToAsync(entryStream);
                }
            }

            var zipBytes = zipMemoryStream.ToArray();
            string zipFileName = $"{baseFileName}_all_parts_{DateTime.Now:yyyyMMddHHmmss}.zip";
            return new FileContentResult(zipBytes, "application/zip") { FileDownloadName = zipFileName };
        }

        private static IEnumerable<string> GenerateCombinations(int combinationSize, int start, int end)
        {
            var buffer = new int[combinationSize];
            return GenerateCombinationsRecursive(buffer, 0, combinationSize, start, end);
        }

        private static IEnumerable<string> GenerateCombinationsRecursive(int[] buffer, int index, int combinationSize, int start, int end)
        {
            if (index == combinationSize)
            {
                yield return string.Join("_", buffer);
                yield break;
            }

            for (int i = start; i <= end; i++)
            {
                buffer[index] = i;
                foreach (var combo in GenerateCombinationsRecursive(buffer, index + 1, combinationSize, i + 1, end))
                    yield return combo;
            }
        }
    }
}