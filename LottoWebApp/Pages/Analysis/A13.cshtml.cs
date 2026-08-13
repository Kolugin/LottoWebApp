using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A13Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A13Model(AppServices s)
        {
            _s = s;
        }

        [BindProperty(SupportsGet = true)]
        public string? Game { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? DrawCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Direction { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        public List<DrawView> Stats { get; set; } = new();
        public List<DrawView> Stats1 { get; set; } = new(); // Для B1
        public List<DrawView> Stats2 { get; set; } = new(); // Для B2
        public List<string> Headers { get; set; } = new();

        public void Dispose()
        {
            Stats?.Clear();
            Stats1?.Clear();
            Stats2?.Clear();
            Headers?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A13_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game)) Game = "keno";

            bool showTime = !(Game.Equals("keno", StringComparison.OrdinalIgnoreCase)
               || Game.Equals("5x36", StringComparison.OrdinalIgnoreCase)
               || Game.Equals("6x49", StringComparison.OrdinalIgnoreCase));

            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

            bool desc = Direction == "newToOld";
            query = desc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (DrawCount.HasValue && DrawCount > 0)
                query = query.Take(DrawCount.Value);

            var result = await query.ToListAsync();
            if (!result.Any()) return;

            // --- ЛОГИКА ДЛЯ 320 ---
            if (Game.Equals("320", StringComparison.OrdinalIgnoreCase))
            {
                int maxNumber = 20;
                int[] prevBalls1 = null;
                int[] prevBalls2 = null;

                foreach (var item in result)
                {
                    var numbers1 = new List<int>();
                    var numbers2 = new List<int>();

                    for (int i = 1; i <= 3; i++)
                    {
                        var prop = item.GetType().GetProperty($"B1{i}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(item);
                            if (int.TryParse(val?.ToString(), out var num) && num > 0)
                                numbers1.Add(num);
                        }
                    }

                    for (int i = 1; i <= 3; i++)
                    {
                        var prop = item.GetType().GetProperty($"B2{i}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(item);
                            if (int.TryParse(val?.ToString(), out var num) && num > 0)
                                numbers2.Add(num);
                        }
                    }

                    var stats1 = Calculate(numbers1.ToArray(), prevBalls1, maxNumber);
                    var stats2 = Calculate(numbers2.ToArray(), prevBalls2, maxNumber);

                    // --- НОВОЕ: Вычисляем SumUnpicked для B1 и B2 отдельно ---
                    int sumUnpicked1 = Enumerable.Range(1, maxNumber).Except(numbers1).Sum();
                    int sumUnpicked2 = Enumerable.Range(1, maxNumber).Except(numbers2).Sum();

                    var draw = new DrawView
                    {
                        Draw = Convert.ToInt32(item.GetType().GetProperty("Draw")?.GetValue(item)),
                        Date = Convert.ToDateTime(item.GetType().GetProperty("Date")?.GetValue(item)),
                        Time = showTime ? item.GetType().GetProperty("Time")?.GetValue(item)?.ToString() ?? "" : "",

                        // Заполняем статистики для B1
                        Sum1 = stats1.Sum,
                        SumUnpicked1 = sumUnpicked1, // <- Новое
                        Average1 = stats1.Average,
                        Median1 = stats1.Median,
                        HasRepeats1 = stats1.HasRepeatsFromPrevious ? "да" : "нет",
                        Variance1 = stats1.Variance,
                        StdDeviation1 = stats1.StdDeviation,
                        Max1 = stats1.Max,
                        Min1 = stats1.Min,
                        EvenCount1 = stats1.EvenCount,
                        OddCount1 = stats1.OddCount,
                        PrimeCount1 = stats1.PrimeCount,
                        CompositeCount1 = stats1.CompositeCount,
                        LowCount1 = stats1.LowCount,
                        HighCount1 = stats1.HighCount,
                        TriangularCount1 = stats1.TriangularCount,
                        SquareCount1 = stats1.SquareCount,
                        MirroredCount1 = stats1.MirroredCount,

                        // Заполняем статистики для B2
                        Sum2 = stats2.Sum,
                        SumUnpicked2 = sumUnpicked2, // <- Новое
                        Average2 = stats2.Average,
                        Median2 = stats2.Median,
                        HasRepeats2 = stats2.HasRepeatsFromPrevious ? "да" : "нет",
                        Variance2 = stats2.Variance,
                        StdDeviation2 = stats2.StdDeviation,
                        Max2 = stats2.Max,
                        Min2 = stats2.Min,
                        EvenCount2 = stats2.EvenCount,
                        OddCount2 = stats2.OddCount,
                        PrimeCount2 = stats2.PrimeCount,
                        CompositeCount2 = stats2.CompositeCount,
                        LowCount2 = stats2.LowCount,
                        HighCount2 = stats2.HighCount,
                        TriangularCount2 = stats2.TriangularCount,
                        SquareCount2 = stats2.SquareCount,
                        MirroredCount2 = stats2.MirroredCount
                    };

                    Stats1.Add(draw);
                    Stats2.Add(draw); // или создать копию, если нужно раздельно

                    prevBalls1 = numbers1.ToArray();
                    prevBalls2 = numbers2.ToArray();
                }

                Headers = new() {
                    "Тираж","Дата","∑ чисел","∑ не выпавших","μ","⌓ Медиана",
                    "Повторения","σ²","σ","↑Max","↓Min","Чет","Нечет","Простые",
                    "Составные","Низкие","Высокие","Треугольные","Квадратные","Зеркальные"
                };
            }
            // --- ОСТАЛЬНЫЕ ИГРЫ (как раньше) ---
            else
            {
                var first = result.First();
                var numberProps = first.GetType().GetProperties()
                    .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                    .OrderBy(p => int.Parse(p.Name.Substring(1)))
                    .ToList();

                int maxNumber = Game switch
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

                    Stats.Add(new DrawView
                    {
                        Draw = Convert.ToInt32(item.GetType().GetProperty("Draw")?.GetValue(item)),
                        Date = Convert.ToDateTime(item.GetType().GetProperty("Date")?.GetValue(item)),
                        Time = showTime ? item.GetType().GetProperty("Time")?.GetValue(item)?.ToString() ?? "" : "",
                        Sum = stats.Sum,
                        SumUnpicked = stats.SumUnpicked,
                        Average = stats.Average,
                        Median = stats.Median,
                        HasRepeats = stats.HasRepeatsFromPrevious ? "да" : "нет",
                        Variance = stats.Variance,
                        StdDeviation = stats.StdDeviation,
                        Max = stats.Max,
                        Min = stats.Min,
                        EvenCount = stats.EvenCount,
                        OddCount = stats.OddCount,
                        PrimeCount = stats.PrimeCount,
                        CompositeCount = stats.CompositeCount,
                        LowCount = stats.LowCount,
                        HighCount = stats.HighCount,
                        TriangularCount = stats.TriangularCount,
                        SquareCount = stats.SquareCount,
                        MirroredCount = stats.MirroredCount
                    });

                    prevBalls = balls;
                }

                Headers = new() {
                    "Тираж","Дата","∑ чисел выпавших","∑ чисел не выпавших","μ число","⌓ Медиана",
                    "Повторения чисел","σ² Дисперсия","σ СКО","↑ Макс","↓ Мин","Четных","Нечетных",
                    "Кол-во простых","Кол-во составных","Кол-во низких","Кол-во высоких",
                    "Кол-во треугольных","Кол-во квадратных","Кол-во зеркальных"
                };
            }
        }

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

        [BindProperty(SupportsGet = true)]
        public string? SelectedTable { get; set; }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            // Вызываем специфичный метод экспорта, передав SelectedTable
            return await _s.Csv13.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}