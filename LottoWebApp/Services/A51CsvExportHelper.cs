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
    public class A51CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A51CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        // 1. Добавили аргумент selectedPosition
        public async Task<FileResult> ExportAsync(string game, int? drawCount, string? direction, bool sortBalls, int selectedPosition)
        {
            var query = new LotteryQueryProvider(_db).GetQuery(game);

            bool isDesc = direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (drawCount.HasValue && drawCount.Value > 0)
                query = query.Take(drawCount.Value);

            var draws = await query.ToListAsync();
            if (!draws.Any()) throw new InvalidOperationException("Нет данных для экспорта.");

            var first = draws.First();

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any()) throw new InvalidOperationException("Не найдены столбцы чисел.");

            (int ballCount, int maxBallNumber) = game?.ToLower() switch
            {
                "keno" => (20, 60),
                "blitz" => (8, 20),
                "5x36" => (6, 36),
                "6x49" => (6, 49),
                "1224" => (12, 24),
                _ => (20, 60)
            };

            var matrix = new int[maxBallNumber, maxBallNumber, ballCount];

            bool isOrderedGame = (game?.ToLower() == "blitz" || game?.ToLower() == "1224");

            if (isOrderedGame)
            {
                await CalculateForOrderedGames(draws, numberProps, ballCount, maxBallNumber, matrix);
            }
            else
            {
                await CalculateForUnorderedGames(draws, numberProps, ballCount, maxBallNumber, sortBalls, matrix);
            }

            // 2. Логика выбора таблицы для экспорта
            string csvContent;
            string fileName;

            if (selectedPosition == -1)
            {
                // Генерация ОБЩЕЙ таблицы
                csvContent = ExportGeneralTransitionTable(matrix, maxBallNumber, ballCount, game, sortBalls);
                fileName = $"5.1_{game.ToUpper()}_Общая_таблица_переходов_{DateTime.Now:yyyyMMddHHmmss}.csv";
            }
            else
            {
                // Проверка на валидность номера позиции
                if (selectedPosition < 0 || selectedPosition >= ballCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(selectedPosition), "Неверный номер позиции.");
                }

                // Генерация таблицы для КОНКРЕТНОЙ ПОЗИЦИИ
                csvContent = ExportTransitionTable(matrix, maxBallNumber, selectedPosition, game, sortBalls);
                fileName = $"5.1_{game.ToUpper()}_Позиция_{selectedPosition + 1}_таблица_переходов_{DateTime.Now:yyyyMMddHHmmss}.csv";
            }

            // 3. Возврат файла из памяти (без сохранения на диск)
            var bytes = Encoding.UTF8.GetBytes(csvContent);
            // Добавляем BOM (Byte Order Mark) для корректного открытия русских букв в Excel
            var preamble = Encoding.UTF8.GetPreamble();
            var completeBytes = new byte[preamble.Length + bytes.Length];
            Buffer.BlockCopy(preamble, 0, completeBytes, 0, preamble.Length);
            Buffer.BlockCopy(bytes, 0, completeBytes, preamble.Length, bytes.Length);

            return new FileContentResult(completeBytes, "text/csv") { FileDownloadName = fileName };
        }

        // Остальные методы (CalculateForOrderedGames, CalculateForUnorderedGames, 
        // ExportTransitionTable, ExportGeneralTransitionTable) оставляем БЕЗ ИЗМЕНЕНИЙ,
        // так как они выполняют только расчеты.

        private async Task CalculateForOrderedGames(List<object> draws, List<System.Reflection.PropertyInfo> numberProps, int ballCount, int maxBallNumber, int[,,] matrix)
        {
            var currentBalls = new List<int>();
            var previousBalls = new List<int>();

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                currentBalls.Clear();
                previousBalls.Clear();

                for (int j = 1; j <= ballCount; j++)
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

                for (int pos = 0; pos < ballCount; pos++)
                {
                    int curr = currentBalls[pos];
                    int prev = previousBalls[pos];

                    if (prev > 0 && prev <= maxBallNumber && curr > 0 && curr <= maxBallNumber)
                    {
                        matrix[prev - 1, curr - 1, pos]++;
                    }
                }
            }
        }

        private async Task CalculateForUnorderedGames(List<object> draws, List<System.Reflection.PropertyInfo> numberProps, int ballCount, int maxBallNumber, bool sortBalls, int[,,] matrix)
        {
            var currentBalls = new List<int>();
            var previousBalls = new List<int>();

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                currentBalls.Clear();
                previousBalls.Clear();

                if (sortBalls)
                {
                    var currentTemp = new List<int>();
                    var previousTemp = new List<int>();
                    for (int j = 1; j <= ballCount; j++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(currentRow);
                            var valPrev = prop.GetValue(previousRow);
                            if (val != null && int.TryParse(val.ToString(), out int currNum) && currNum > 0)
                            {
                                currentTemp.Add(currNum);
                            }
                            if (valPrev != null && int.TryParse(valPrev.ToString(), out int prevNum) && prevNum > 0)
                            {
                                previousTemp.Add(prevNum);
                            }
                        }
                    }
                    currentTemp.Sort();
                    previousTemp.Sort();
                    currentBalls.AddRange(currentTemp);
                    previousBalls.AddRange(previousTemp);
                    while (currentBalls.Count < ballCount) currentBalls.Add(0);
                    while (previousBalls.Count < ballCount) previousBalls.Add(0);
                }
                else
                {
                    for (int j = 1; j <= ballCount; j++)
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

                    var currentFiltered = currentBalls.Where(x => x != 0).ToList();
                    var previousFiltered = previousBalls.Where(x => x != 0).ToList();
                    currentFiltered.AddRange(Enumerable.Repeat(0, ballCount - currentFiltered.Count));
                    previousFiltered.AddRange(Enumerable.Repeat(0, ballCount - previousFiltered.Count));
                    currentBalls = currentFiltered;
                    previousBalls = previousFiltered;
                }

                if (sortBalls)
                {
                    for (int pos = 0; pos < ballCount; pos++)
                    {
                        int curr = currentBalls[pos];
                        int prev = previousBalls[pos];

                        if (prev > 0 && prev <= maxBallNumber && curr > 0 && curr <= maxBallNumber)
                        {
                            matrix[prev - 1, curr - 1, pos]++;
                        }
                    }
                }
                else
                {
                    for (int pos = 0; pos < ballCount; pos++)
                    {
                        int curr = currentBalls[pos];
                        for (int nextPos = 0; nextPos < ballCount; nextPos++)
                        {
                            int prev = previousBalls[nextPos];

                            if (prev > 0 && prev <= maxBallNumber && curr > 0 && curr <= maxBallNumber)
                            {
                                matrix[prev - 1, curr - 1, pos]++;
                            }
                        }
                    }
                }
            }
        }

        private string ExportTransitionTable(int[,,] matrix, int maxBallNumber, int position, string game, bool sortBalls)
        {
            var csvContent = new StringBuilder();
            csvContent.AppendLine($"Таблица переходов для игры {game}{(sortBalls ? " (отсортировано)" : "")}, Столбец {position + 1}");
            csvContent.AppendLine("№ Шара пред./тек.;" + string.Join(";", Enumerable.Range(1, maxBallNumber)));

            for (int number = 1; number <= maxBallNumber; number++)
            {
                var row = new List<string> { number.ToString() };
                int totalTransitions = 0;
                for (int j = 0; j < maxBallNumber; j++)
                {
                    totalTransitions += matrix[number - 1, j, position];
                }

                for (int nextNumber = 1; nextNumber <= maxBallNumber; nextNumber++)
                {
                    double probability = totalTransitions > 0
                        ? (double)matrix[number - 1, nextNumber - 1, position] / totalTransitions
                        : 0;
                    row.Add(probability > 0 ? (probability * 100).ToString("F4") + "%" : "-");
                }

                csvContent.AppendLine(string.Join(";", row));
            }

            return csvContent.ToString();
        }

        private string ExportGeneralTransitionTable(int[,,] matrix, int maxBallNumber, int numberOfBalls, string game, bool sortBalls)
        {
            var csvContent = new StringBuilder();
            csvContent.AppendLine($"Общая таблица переходов для игры {game}{(sortBalls ? " (отсортировано)" : "")}");
            csvContent.AppendLine("№ Шара пред./тек.;" + string.Join(";", Enumerable.Range(1, maxBallNumber)));

            for (int number = 1; number <= maxBallNumber; number++)
            {
                var row = new List<string> { number.ToString() };
                for (int nextNumber = 1; nextNumber <= maxBallNumber; nextNumber++)
                {
                    double totalProbabilitySum = 0;
                    int validPositions = 0;

                    for (int pos = 0; pos < numberOfBalls; pos++)
                    {
                        int totalTransitions = 0;
                        for (int j = 0; j < maxBallNumber; j++)
                        {
                            totalTransitions += matrix[number - 1, j, pos];
                        }

                        if (totalTransitions > 0)
                        {
                            totalProbabilitySum += (double)matrix[number - 1, nextNumber - 1, pos] / totalTransitions;
                            validPositions++;
                        }
                    }

                    double averageProbability = validPositions > 0 ? totalProbabilitySum / validPositions : 0;
                    row.Add(averageProbability > 0 ? (averageProbability * 100).ToString("F4") + "%" : "-");
                }
                csvContent.AppendLine(string.Join(";", row));
            }

            return csvContent.ToString();
        }
    }
}