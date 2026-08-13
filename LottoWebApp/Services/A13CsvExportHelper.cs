using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Reflection; 

namespace LottoWebApp.Services
{
    public class A13CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A13CsvExportHelper(LottoDbContext db)
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

            var result = await query.ToListAsync();
            if (!result.Any())
                throw new InvalidOperationException("Нет данных для экспорта.");

            // --- ЛОГИКА ДЛЯ 320 ---
            if (game.Equals("320", StringComparison.OrdinalIgnoreCase))
            {
                // Определяем, какую таблицу экспортировать
                string exportTable = selectedTable?.ToLowerInvariant() switch
                {
                    "b1" => "B1",
                    "b2" => "B2",
                    _ => "B1" // по умолчанию
                };

                DataTable dt = CreateDataTableFor320Field(exportTable);

                int maxNumber = 20;
                int[] prevBalls = null; // для повторов между тиражами

                foreach (var item in result)
                {
                    var numbers = new List<int>();

                    for (int i = 1; i <= 3; i++)
                    {
                        string propName = exportTable == "B1" ? $"B1{i}" : $"B2{i}";
                        var prop = item.GetType().GetProperty(propName);
                        if (prop != null)
                        {
                            var val = prop.GetValue(item);
                            if (int.TryParse(val?.ToString(), out var num) && num > 0)
                                numbers.Add(num);
                        }
                    }

                    var stats = Calculate(numbers.ToArray(), prevBalls, maxNumber);

                    var row = dt.NewRow();
                    FillRow(dt, row, item, stats);
                    dt.Rows.Add(row);

                    prevBalls = numbers.ToArray();
                }

                // Имя файла
                string titlePart = string.IsNullOrEmpty(title) ? "A13_Export" : title;
                string drawCountStr = drawCount?.ToString() ?? "all";
                string safeFileName = $"{titlePart}_{game}_{exportTable}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
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
            // --- ОСТАЛЬНЫЕ ИГРЫ ---
            else
            {
                // Создание DataTable - ЛОГИКА ТАКАЯ ЖЕ, КАК В A13Model.OnGetAsync (расчёт статистики)
                DataTable dt = CreateDataTableForOtherGames(game);

                var first = result.First();
                var numberProps = first.GetType().GetProperties()
                    .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                    .OrderBy(p => int.Parse(p.Name.Substring(1)))
                    .ToList();

                int maxNumber = game switch
                {
                    "keno" => 60,
                    "blitz" => 20,
                    "5x36" => 36,
                    "6x49" => 49,
                    "1224" => 24,
                    _ => 60
                };

                int[] prevBalls = null;

                foreach (var item in result)
                {
                    var balls = numberProps
                        .Select(p => Convert.ToInt32(p.GetValue(item)))
                        .Where(v => v > 0)
                        .ToArray();

                    var stats = Calculate(balls, prevBalls, maxNumber);

                    var row = dt.NewRow();
                    FillRow(dt, row, item, stats);
                    dt.Rows.Add(row);
                    prevBalls = balls;
                }

                // Имя файла
                string titlePart = string.IsNullOrEmpty(title) ? "A13_Export" : title;
                string drawCountStr = drawCount?.ToString() ?? "all";

                string safeFileName = $"{titlePart}_{game}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
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
        }
        private DataTable CreateDataTableFor320Field(string field)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("Тираж");
            dt.Columns.Add("Дата");
            dt.Columns.Add("Время");

            // Заголовки статистики
            var statHeaders = new[] {
                "∑ чисел", "∑ не выпавших", "μ", "⌓ Медиана",
                "Повторения", "σ²", "σ", "↑Max", "↓Min", "Чет", "Нечет",
                "Простые", "Составные", "Низкие", "Высокие",
                "Треугольные", "Квадратные", "Зеркальные"
            };
            foreach (var header in statHeaders)
            {
                dt.Columns.Add(header);
            }

            return dt;
        }

        private DataTable CreateDataTableForOtherGames(string game)
        {
            DataTable dt = new DataTable();
            dt.Columns.Add("Тираж");
            dt.Columns.Add("Дата");

            bool showTime = !(game.Equals("keno", StringComparison.OrdinalIgnoreCase)
                             || game.Equals("5x36", StringComparison.OrdinalIgnoreCase)
                             || game.Equals("6x49", StringComparison.OrdinalIgnoreCase));
            if (showTime)
                dt.Columns.Add("Время");

            // Заголовки статистики
            var statHeaders = new[] {
                "∑ чисел выпавших","∑ чисел не выпавших","μ число","⌓ Медиана",
                "Повторения чисел","σ² Дисперсия","σ СКО","↑ Макс","↓ Мин","Четных","Нечетных",
                "Кол-во простых","Кол-во составных","Кол-во низких","Кол-во высоких",
                "Кол-во треугольных","Кол-во квадратных","Кол-во зеркальных"
            };
            foreach (var header in statHeaders)
            {
                dt.Columns.Add(header);
            }

            return dt;
        }
        private void FillRow(DataTable dt, DataRow row, object item, DrawStatistics stats)
        {
            int col = 0;
            row[col++] = Convert.ToInt32(item.GetType().GetProperty("Draw")?.GetValue(item));
            row[col++] = Convert.ToDateTime(item.GetType().GetProperty("Date")?.GetValue(item)).ToString("dd.MM.yyyy");

            if (dt.Columns.Contains("Время"))
            {
                row[col++] = item.GetType().GetProperty("Time")?.GetValue(item)?.ToString() ?? "";
            }

            // Заполняем статистические столбцы
            row[col++] = stats.Sum;
            row[col++] = stats.SumUnpicked;
            row[col++] = stats.Average;
            row[col++] = stats.Median;
            row[col++] = stats.HasRepeatsFromPrevious ? "да" : "нет";
            row[col++] = stats.Variance;
            row[col++] = stats.StdDeviation;
            row[col++] = stats.Max;
            row[col++] = stats.Min;
            row[col++] = stats.EvenCount;
            row[col++] = stats.OddCount;
            row[col++] = stats.PrimeCount;
            row[col++] = stats.CompositeCount;
            row[col++] = stats.LowCount;
            row[col++] = stats.HighCount;
            row[col++] = stats.TriangularCount;
            row[col++] = stats.SquareCount;
            row[col++] = stats.MirroredCount;
        }
        // Внутренний метод для расчёта статистики (копия из A13Model)
        private class DrawStatistics
        {
            public int Sum { get; set; }
            public int SumUnpicked { get; set; }
            public double Average { get; set; }
            public double Median { get; set; }
            public bool HasRepeatsFromPrevious { get; set; }
            public double Variance { get; set; }
            public double StdDeviation { get; set; }
            public int Max { get; set; }
            public int Min { get; set; }
            public int EvenCount { get; set; }
            public int OddCount { get; set; }
            public int PrimeCount { get; set; }
            public int CompositeCount { get; set; }
            public int LowCount { get; set; }
            public int HighCount { get; set; }
            public int TriangularCount { get; set; }
            public int SquareCount { get; set; }
            public int MirroredCount { get; set; }
        }

        private DrawStatistics Calculate(int[] balls, int[] previousBalls, int maxNumber)
        {
            // Эти вспомогательные методы можно вынести в отдельный общий класс,
            // но для ясности оставим их тут
            double GetMedian(int[] n)
            {
                var s = n.OrderBy(x => x).ToArray();
                int c = s.Length;
                return (c % 2 == 0) ? (s[c / 2 - 1] + s[c / 2]) / 2.0 : s[c / 2];
            }

            bool IsPrime(int n)
            {
                if (n < 2) return false;
                for (int i = 2; i <= Math.Sqrt(n); i++)
                    if (n % i == 0) return false;
                return true;
            }

            bool IsPerfectSquare(int n)
            {
                int r = (int)Math.Sqrt(n);
                return r * r == n;
            }

            bool IsTriangular(int n)
            {
                int k = (int)((Math.Sqrt(8 * n + 1) - 1) / 2);
                return k * (k + 1) / 2 == n;
            }

            bool IsMirrored(int n)
            {
                string s = n.ToString();
                return s == new string(s.Reverse().ToArray());
            }

            var stats = new DrawStatistics
            {
                Sum = balls.Sum(),
                Average = Math.Round(balls.Average(), 4),
                Median = Math.Round(GetMedian(balls), 4),
                Max = balls.Max(),
                Min = balls.Min(),
                EvenCount = balls.Count(b => b % 2 == 0),
                OddCount = balls.Count(b => b % 2 != 0),
                PrimeCount = balls.Count(IsPrime),
                CompositeCount = balls.Count(b => b > 1 && !IsPrime(b)),
                LowCount = balls.Count(b => b <= maxNumber / 2),
                HighCount = balls.Count(b => b > maxNumber / 2),
                TriangularCount = balls.Count(IsTriangular),
                SquareCount = balls.Count(IsPerfectSquare),
                MirroredCount = balls.Count(IsMirrored)
            };

            double mean = stats.Average;
            stats.Variance = Math.Round(balls.Select(v => Math.Pow(v - mean, 2)).Average(), 4);
            stats.StdDeviation = Math.Round(Math.Sqrt(stats.Variance), 4);

            if (previousBalls != null)
                stats.HasRepeatsFromPrevious = balls.Intersect(previousBalls).Any();

            stats.SumUnpicked = Enumerable.Range(1, maxNumber).Except(balls).Sum();

            return stats;
        }
    }
}