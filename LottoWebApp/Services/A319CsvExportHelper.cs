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
    public class A319CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A319CsvExportHelper(LottoDbContext db)
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

            var distribution = new Dictionary<(int Triangular, int Square, int Mirrored), int>();
            int totalDraws = 0;

            foreach (var row in draws)
            {
                var balls = ExtractBallsFromRow(row, numberProps, numberOfBalls);
                if (balls.Length == 0) continue;

                var stats = CalculateDrawStats(balls, maxBallNumber);
                var key = (stats.TriangularCount, stats.SquareCount, stats.MirroredCount);

                if (!distribution.ContainsKey(key))
                    distribution[key] = 0;
                distribution[key]++;
                totalDraws++;
            }

            DataTable dt = new DataTable();
            dt.Columns.Add("Треугольные числа", typeof(int));
            dt.Columns.Add("Квадратные числа", typeof(int));
            dt.Columns.Add("Зеркальные числа", typeof(int));
            dt.Columns.Add("Частота", typeof(int));
            dt.Columns.Add("Шанс %", typeof(double));

            foreach (var kvp in distribution.OrderBy(x => x.Key))
            {
                int triangular = kvp.Key.Triangular;
                int square = kvp.Key.Square;
                int mirrored = kvp.Key.Mirrored;
                int frequency = kvp.Value;
                double chance = Math.Round(frequency * 100.0 / totalDraws, 4);

                dt.Rows.Add(triangular, square, mirrored, frequency, chance);
            }

            string titlePart = string.IsNullOrEmpty(title) ? "A319_Triangular_Square_Mirrored_Distribution" : title;
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

        private int[] ExtractBallsFromRow(object row, List<System.Reflection.PropertyInfo> numberProps, int numberOfBalls)
        {
            var balls = new List<int>();
            for (int i = 1; i <= numberOfBalls; i++)
            {
                var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                if (prop != null)
                {
                    var val = prop.GetValue(row);
                    if (val != null && int.TryParse(val.ToString(), out int ball) && ball != 0)
                    {
                        balls.Add(ball);
                    }
                }
            }
            return balls.ToArray();
        }

        private (int TriangularCount, int SquareCount, int MirroredCount) CalculateDrawStats(int[] balls, int maxBallNumber)
        {
            if (balls.Length == 0) return (0, 0, 0);

            int triangularCount = 0;
            int squareCount = 0;
            int mirroredCount = 0;

            foreach (int ball in balls)
            {
                if (ball > 0 && ball <= maxBallNumber)
                {
                    if (IsTriangularNumber(ball))
                        triangularCount++;
                    if (IsPerfectSquare(ball))
                        squareCount++;
                    if (IsMirroredNumber(ball))
                        mirroredCount++;
                }
            }

            return (triangularCount, squareCount, mirroredCount);
        }

        private bool IsTriangularNumber(int number)
        {
            if (number < 1) return false;
            // Формула: n(n+1)/2 = number => n^2 + n - 2*number = 0
            double n = (-1 + Math.Sqrt(1 + 8 * number)) / 2;
            return n == Math.Floor(n);
        }

        private bool IsPerfectSquare(int number)
        {
            if (number < 1) return false;
            int sqrt = (int)Math.Sqrt(number);
            return sqrt * sqrt == number;
        }

        private bool IsMirroredNumber(int number)
        {
            if (number < 10) return false; // Однозначные числа не считаются зеркальными
            string str = number.ToString();
            char[] chars = str.ToCharArray();
            Array.Reverse(chars);
            string reversed = new string(chars);
            return str == reversed;
        }
    }
}