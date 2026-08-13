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
    public class A54CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A54CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, bool sortBalls, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            var query = new LotteryQueryProvider(_db).GetQuery(game);

            bool newestFirst = direction == "newToOld";

            // «Сначала старые» — просто берём первые N
            if (!newestFirst)
            {
                query = query.OrderBy(e => EF.Property<int>(e, "Draw"));

                if (drawCount.HasValue && drawCount.Value > 0)
                    query = query.Take(drawCount.Value);
            }
            // «Сначала новые» — берём с конца, затем разворачиваем по возрастанию
            else
            {
                query = query.OrderByDescending(e => EF.Property<int>(e, "Draw"));

                if (drawCount.HasValue && drawCount.Value > 0)
                    query = query.Take(drawCount.Value);

                query = query.OrderBy(e => EF.Property<int>(e, "Draw"));
            }

            var draws = await query.ToListAsync();
            //=====================================================================================
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
            int numberOfBalls, maxBallNumber;
            (numberOfBalls, maxBallNumber) = game?.ToLower() switch
            {
                "keno" => (20, 60),
                "blitz" => (8, 20),
                "5x36" => (6, 36),
                "6x49" => (6, 49),
                "1224" => (12, 24),
                _ => (20, 60)
            };

            var matrix = new int[maxBallNumber, maxBallNumber, numberOfBalls];

            bool isOrdered = (game?.ToLower() == "blitz" || game?.ToLower() == "1224");

            if (isOrdered)
                await CalculateForOrderedGames(draws, numberProps, matrix, numberOfBalls);
            else
                await CalculateForUnorderedGames(draws, numberProps, matrix, sortBalls, numberOfBalls);

            var lastDraw = draws.Last();
            var lastBalls = new int[numberOfBalls];

            for (int i = 0; i < numberOfBalls; i++)
            {
                var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i + 1}");
                if (prop != null)
                {
                    var val = prop.GetValue(lastDraw);
                    if (val != null && int.TryParse(val.ToString(), out int num))
                    {
                        lastBalls[i] = num;
                    }
                }
            }

            if (sortBalls)
            {
                lastBalls = lastBalls.Where(x => x > 0).OrderBy(x => x).ToArray();
                if (lastBalls.Length < numberOfBalls)
                {
                    var zeros = Enumerable.Repeat(0, numberOfBalls - lastBalls.Length);
                    lastBalls = lastBalls.Concat(zeros).ToArray();
                }
            }

            // Получение списка вероятных чисел по позициям с сортировкой по убыванию веса
            var predictedByPosition = new List<List<int>>();
            for (int pos = 0; pos < numberOfBalls; pos++)
            {
                var possible = new List<(int Number, int Weight)>();
                int prev = lastBalls[pos];

                if (prev > 0 && prev <= maxBallNumber)
                {
                    for (int next = 0; next < maxBallNumber; next++)
                    {
                        int weight = matrix[prev - 1, next, pos];
                        if (weight > 0)
                            possible.Add((next + 1, weight));
                    }
                }

                predictedByPosition.Add(
                    possible
                        .OrderByDescending(p => p.Weight)
                        .Select(p => p.Number)
                        .ToList()
                );
            }

            int maxRows = predictedByPosition.Any() ? predictedByPosition.Max(list => list.Count) : 0;

            // Создаем DataTable
            DataTable dt = new DataTable();
            for (int pos = 0; pos < numberOfBalls; pos++)
            {
                dt.Columns.Add($"№{pos + 1}", typeof(int));
            }

            for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
            {
                var newRow = dt.NewRow();
                for (int pos = 0; pos < numberOfBalls; pos++)
                {
                    var list = predictedByPosition[pos];
                    newRow[pos] = (rowIndex < list.Count) ? list[rowIndex] : DBNull.Value;
                }
                dt.Rows.Add(newRow);
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A54_Predicted_Numbers_By_Position" : title;
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

        private async Task CalculateForOrderedGames(List<object> draws, List<System.Reflection.PropertyInfo> numberProps, int[,,] matrix, int numberOfBalls)
        {
            var currentBalls = new List<int>();
            var previousBalls = new List<int>();

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                currentBalls.Clear();
                previousBalls.Clear();

                for (int j = 1; j <= numberOfBalls; j++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(currentRow);
                        var valPrev = prop.GetValue(previousRow);
                        if (val != null && int.TryParse(val.ToString(), out int currNum))
                        {
                            currentBalls.Add(currNum);
                        }
                        else
                        {
                            currentBalls.Add(0);
                        }

                        if (valPrev != null && int.TryParse(valPrev.ToString(), out int prevNum))
                        {
                            previousBalls.Add(prevNum);
                        }
                        else
                        {
                            previousBalls.Add(0);
                        }
                    }
                    else
                    {
                        currentBalls.Add(0);
                        previousBalls.Add(0);
                    }
                }

                for (int pos = 0; pos < numberOfBalls; pos++)
                {
                    int curr = currentBalls[pos];
                    int prev = previousBalls[pos];

                    if (prev > 0 && prev <= matrix.GetLength(0) && curr > 0 && curr <= matrix.GetLength(1))
                    {
                        matrix[prev - 1, curr - 1, pos]++;
                    }
                }
            }
        }

        private async Task CalculateForUnorderedGames(List<object> draws, List<System.Reflection.PropertyInfo> numberProps, int[,,] matrix, bool sortBalls, int numberOfBalls)
        {
            var currentBalls = new List<int>();
            var previousBalls = new List<int>();

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                currentBalls.Clear();
                previousBalls.Clear();

                for (int j = 1; j <= numberOfBalls; j++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(currentRow);
                        var valPrev = prop.GetValue(previousRow);
                        if (val != null && int.TryParse(val.ToString(), out int currNum))
                        {
                            currentBalls.Add(currNum);
                        }
                        else
                        {
                            currentBalls.Add(0);
                        }

                        if (valPrev != null && int.TryParse(valPrev.ToString(), out int prevNum))
                        {
                            previousBalls.Add(prevNum);
                        }
                        else
                        {
                            previousBalls.Add(0);
                        }
                    }
                    else
                    {
                        currentBalls.Add(0);
                        previousBalls.Add(0);
                    }
                }

                if (sortBalls)
                {
                    currentBalls = currentBalls.Where(x => x != 0).OrderBy(x => x).ToList();
                    previousBalls = previousBalls.Where(x => x != 0).OrderBy(x => x).ToList();

                    currentBalls.AddRange(Enumerable.Repeat(0, numberOfBalls - currentBalls.Count));
                    previousBalls.AddRange(Enumerable.Repeat(0, numberOfBalls - previousBalls.Count));

                    for (int pos = 0; pos < numberOfBalls; pos++)
                    {
                        int curr = currentBalls[pos];
                        int prev = previousBalls[pos];

                        if (prev > 0 && prev <= matrix.GetLength(0) && curr > 0 && curr <= matrix.GetLength(1))
                        {
                            matrix[prev - 1, curr - 1, pos]++;
                        }
                    }
                }
                else
                {
                    for (int pos = 0; pos < numberOfBalls; pos++)
                    {
                        int curr = currentBalls[pos];
                        for (int nextPos = 0; nextPos < numberOfBalls; nextPos++)
                        {
                            int prev = previousBalls[nextPos];

                            if (prev > 0 && prev <= matrix.GetLength(0) && curr > 0 && curr <= matrix.GetLength(1))
                            {
                                matrix[prev - 1, curr - 1, pos]++;
                            }
                        }
                    }
                }
            }
        }
    }
}