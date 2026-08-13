using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LottoWebApp.Services
{
    public class A321CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A321CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }
        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? title, bool sortBalls)
        {
            if (string.IsNullOrEmpty(game)) game = "keno";
            if (game.ToLower() == "blitz" || game.ToLower() == "1224")
            {
                sortBalls = false;
            }
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

            if (isDesc)
            {
                draws.Reverse();
            }

            var first = draws.First();

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            // Определяем параметры для игры
            int maxBallNumber, numberOfBalls;
            (maxBallNumber, numberOfBalls) = game.ToLower() switch
            {
                "keno" => (60, 20),
                "blitz" => (20, 8),
                "5x36" => (36, 6),
                "6x49" => (49, 6),
                "1224" => (24, 12),
                _ => (60, 20)
            };
            bool hasTime = game.ToLower() == "blitz" || game.ToLower() == "1224";

            var drawProp = first.GetType().GetProperty("Draw");
            var dateProp = first.GetType().GetProperty("Date");
            var timeProp = hasTime ? first.GetType().GetProperty("Time") : null;

            // Хранилище для сводной таблицы
            var summaryTransitions = new List<(string DrawNumber, string DrawDate, string? DrawTime, List<string> Transitions)>();
            // Хранилище для детальных таблиц вероятностей
            var detailedProbabilities = new Dictionary<int, Dictionary<int, List<int>>>();

            // Инициализируем хранилище для вероятностей по всем позициям
            for (int p = 0; p < numberOfBalls; p++)
            {
                detailedProbabilities.Add(p, new Dictionary<int, List<int>>());
            }

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                var previousBalls = GetBallsFromDraw(previousRow, numberOfBalls, numberProps);
                var currentBalls = GetBallsFromDraw(currentRow, numberOfBalls, numberProps);

                // Логика сортировки
                if (sortBalls)
                {
                    previousBalls = previousBalls.Where(x => x != 0).OrderBy(x => x).ToList();
                    currentBalls = currentBalls.Where(x => x != 0).OrderBy(x => x).ToList();

                    while (previousBalls.Count < numberOfBalls) previousBalls.Add(0);
                    while (currentBalls.Count < numberOfBalls) currentBalls.Add(0);
                }

                // Получаем информацию о тираже
                var drawNumber = drawProp?.GetValue(currentRow)?.ToString() ?? (i + 1).ToString();
                var drawDate = dateProp?.GetValue(currentRow)?.ToString() ?? "";
                var drawTime = hasTime && timeProp != null ? timeProp.GetValue(currentRow)?.ToString() : null;

                var transitionsList = new List<string>();

                for (int pos = 0; pos < numberOfBalls; pos++)
                {
                    int prevBall = 0;
                    int currBall = 0;

                    if (pos < previousBalls.Count && pos < currentBalls.Count)
                    {
                        prevBall = previousBalls[pos];
                        currBall = currentBalls[pos];

                        // Заполнение данных для детальной таблицы вероятностей
                        if (prevBall > 0 && currBall > 0)
                        {
                            var posTransitions = detailedProbabilities[pos];
                            if (!posTransitions.ContainsKey(prevBall))
                                posTransitions[prevBall] = new List<int>();

                            posTransitions[prevBall].Add(currBall);
                        }
                    }
                    transitionsList.Add($"{prevBall} -> {currBall}");
                }

                summaryTransitions.Add((drawNumber, drawDate, drawTime, transitionsList));
            }

            var finalCsvContent = new StringBuilder();
            finalCsvContent.AppendLine($"=========================================================================================================================================");
            finalCsvContent.AppendLine($"СВОДНАЯ ТАБЛИЦА ПЕРЕХОДОВ (игра {game}, тиражей {draws.Count - 1})");
            finalCsvContent.AppendLine($"Настройки: Шары {(sortBalls ? "Сортированы" : "Позиционные")}, Направление {(isDesc ? "Новые -> Старые" : "Старые -> Новые")}");
            finalCsvContent.AppendLine($"=========================================================================================================================================");

            // Заголовок сводной таблицы
            finalCsvContent.Append("Тираж;Дата;");
            if (hasTime) finalCsvContent.Append("Время;");

            for (int pos = 1; pos <= numberOfBalls; pos++)
            {
                finalCsvContent.Append($"№П {pos};");
            }
            finalCsvContent.AppendLine();

            // Тело сводной таблицы
            foreach (var record in summaryTransitions)
            {
                finalCsvContent.Append($"{record.DrawNumber};{record.DrawDate};");
                if (hasTime) finalCsvContent.Append($"{record.DrawTime};");

                finalCsvContent.Append(string.Join(";", record.Transitions));
                finalCsvContent.AppendLine();
            }

            finalCsvContent.AppendLine();
            finalCsvContent.AppendLine();

            for (int pos = 0; pos < numberOfBalls; pos++)
            {
                var allTransitions = detailedProbabilities[pos];

                finalCsvContent.AppendLine($"=========================================================================================================================================");
                finalCsvContent.AppendLine($"Таблица вероятностей переходов для игры {game}, Позиция {pos + 1} (Всего тиражей: {draws.Count - 1})");
                finalCsvContent.AppendLine($"=========================================================================================================================================");

                ExportProbabilityTable(finalCsvContent, allTransitions, maxBallNumber);
                finalCsvContent.AppendLine();
            }

            // 6. ВОЗВРАТ ФАЙЛА
            string titlePart = string.IsNullOrEmpty(title) ? "A321_Transition_Map_By_Draw" : title;
            string gameStr = game ?? "unknown";
            string drawCountStr = drawCount?.ToString() ?? "all";

            string safeFileName = $"3.21 Карта переходов_{titlePart}_{gameStr}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
                .Replace(" ", "_")
                .Replace(":", "_")
                .Replace("/", "_")
                .Replace("\\", "_");
            var encoding = Encoding.GetEncoding(1251);
            var bytes = encoding.GetBytes(finalCsvContent.ToString());

            // Возвращаем FileContentResult
            return new FileContentResult(bytes, "text/csv")
            {
                FileDownloadName = safeFileName
            };
        }
        private List<int> GetBallsFromDraw(object draw, int ballCount, List<PropertyInfo> numberProps)
        {
            var balls = new List<int>();
            for (int j = 1; j <= ballCount; j++)
            {
                // Ищем свойство B1, B2, ...
                var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}" || (j == ballCount && p.Name == "BB"));

                int ballValue = 0;
                if (prop != null)
                {
                    var val = prop.GetValue(draw);
                    if (val != null && int.TryParse(val.ToString(), out ballValue))
                    {
                        
                    }
                }
                balls.Add(ballValue);
            }
            return balls;
        }
        private void ExportProbabilityTable(StringBuilder csvContent, Dictionary<int, List<int>> allTransitions, int matrixSize)
        {
            if (allTransitions.Count == 0) return;

            // Заголовок таблицы вероятностей
            csvContent.Append("№ Шара пред./тек.;");
            for (int j = 1; j <= matrixSize; j++)
            {
                csvContent.Append($"{j};");
            }
            csvContent.AppendLine();
            // Для каждого предыдущего шара показываем вероятности
            foreach (var kvp in allTransitions.OrderBy(x => x.Key))
            {
                int prevBall = kvp.Key;
                var nextBalls = kvp.Value;

                csvContent.Append($"{prevBall}");
                // Считаем частоты для каждого следующего шара
                var frequency = new Dictionary<int, int>();
                foreach (int nextBall in nextBalls)
                {
                    if (nextBall > 0)
                    {
                        if (!frequency.ContainsKey(nextBall)) frequency[nextBall] = 0;
                        frequency[nextBall]++;
                    }
                }

                int totalCount = nextBalls.Count;
                if (totalCount == 0)
                {
                    // Добавляем пустые ячейки, если нет переходов, чтобы сохранить структуру
                    for (int j = 1; j <= matrixSize; j++) csvContent.Append(";");
                    csvContent.AppendLine();
                    continue;
                }
                // Показываем вероятности для всех возможных шаров
                for (int j = 1; j <= matrixSize; j++)
                {
                    csvContent.Append(";"); // Разделитель перед значением
                    if (frequency.ContainsKey(j))
                    {
                        double probability = (double)frequency[j] / totalCount;
                        csvContent.Append(probability.ToString("F4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        csvContent.Append("-");
                    }
                }
                csvContent.AppendLine();
            }
        }
    }
}