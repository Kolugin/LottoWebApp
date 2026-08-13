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
    public class A53CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A53CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? title, int selectedBall)
        {
            if (string.IsNullOrEmpty(game)) game = "keno";

            // 1. Получаем данные (этот блок оставляем как был у вас)
            var query = new LotteryQueryProvider(_db).GetQuery(game);
            bool isDesc = direction == "newToOld";
            query = isDesc ? query.OrderByDescending(e => EF.Property<int>(e, "Draw")) : query.OrderBy(e => EF.Property<int>(e, "Draw"));
            if (drawCount.HasValue && drawCount.Value > 0) query = query.Take(drawCount.Value);

            var draws = await query.ToListAsync();
            if (!draws.Any()) throw new InvalidOperationException("Нет данных.");

            // 2. Определяем параметры игры (как в вашем коде)
            int numberOfBalls, maxColumn;
            (numberOfBalls, maxColumn) = game.ToLower() switch
            {
                "keno" => (60, 20),
                "blitz" => (20, 8),
                "5x36" => (36, 6),
                "6x49" => (49, 6),
                "1224" => (24, 12),
                _ => (60, 20)
            };

            // 3. Считаем матрицу ТОЛЬКО для одного выбранного шара Y (экономим память)
            int maxIntervals = 10;
            var matrixForSelectedBall = new Dictionary<int, int[,]>(); // [Z][Interval, Pos]
            for (int z = 1; z <= numberOfBalls; z++)
                matrixForSelectedBall[z] = new int[maxIntervals, maxColumn];

            var numberProps = draws.First().GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1))).ToList();

            for (int i = 0; i < draws.Count - maxIntervals; i++)
            {
                // Находим, был ли выбранный шар в текущем тираже
                bool ballYPresent = false;
                for (int j = 1; j <= maxColumn; j++)
                {
                    var val = numberProps.FirstOrDefault(p => p.Name == $"B{j}")?.GetValue(draws[i]);
                    if (val != null && int.Parse(val.ToString()) == selectedBall)
                    {
                        ballYPresent = true; break;
                    }
                }

                if (!ballYPresent) continue;

                // Если шар был, считаем переходы через интервалы
                for (int interval = 1; interval <= maxIntervals; interval++)
                {
                    if (i + interval >= draws.Count) break;
                    for (int k = 1; k <= maxColumn; k++)
                    {
                        var val = numberProps.FirstOrDefault(p => p.Name == $"B{k}")?.GetValue(draws[i + interval]);
                        if (val != null && int.TryParse(val.ToString(), out int futureBall))
                        {
                            if (futureBall > 0 && futureBall <= numberOfBalls)
                                matrixForSelectedBall[futureBall][interval - 1, k - 1]++;
                        }
                    }
                }
            }

            // 4. Генерируем CSV контент
            var csvContent = new StringBuilder();
            csvContent.AppendLine($"Вероятности переходов для числа Y = {selectedBall};Игра: {game}");

            var header = new List<string> { "Число Z" };
            for (int interval = 1; interval <= maxIntervals; interval++)
                for (int pos = 1; pos <= maxColumn; pos++)
                    header.Add($"И {interval} П {pos}");
            csvContent.AppendLine(string.Join(";", header));

            for (int z = 1; z <= numberOfBalls; z++)
            {
                var row = new List<string> { z.ToString() };
                int total = 0;
                for (int i = 0; i < maxIntervals; i++)
                    for (int j = 0; j < maxColumn; j++)
                        total += matrixForSelectedBall[z][i, j];

                for (int interval = 0; interval < maxIntervals; interval++)
                    for (int pos = 0; pos < maxColumn; pos++)
                    {
                        double prob = total > 0 ? (double)matrixForSelectedBall[z][interval, pos] / total : 0;
                        row.Add($"{prob:F4}");
                    }
                csvContent.AppendLine(string.Join(";", row));
            }

            // 5. Возвращаем файл с кодировкой UTF-8 + BOM
            string fileName = $"5.3_{game.ToUpper()}_Шар_{selectedBall}_{DateTime.Now:yyyyMMddHHmmss}.csv";

            // Создаем массив байтов из строки
            byte[] buffer = Encoding.UTF8.GetBytes(csvContent.ToString());

            // Создаем префикс BOM для UTF-8
            byte[] bom = Encoding.UTF8.GetPreamble(); // Это и есть { 0xEF, 0xBB, 0xBF }

            // Объединяем BOM и основные данные
            byte[] fileResult = new byte[bom.Length + buffer.Length];
            Buffer.BlockCopy(bom, 0, fileResult, 0, bom.Length);
            Buffer.BlockCopy(buffer, 0, fileResult, bom.Length, buffer.Length);

            return new FileContentResult(fileResult, "text/csv; charset=utf-8")
            {
                FileDownloadName = fileName
            };
        }
    }
}