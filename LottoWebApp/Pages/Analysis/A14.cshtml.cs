using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A14Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A14Model(AppServices s)
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

        // Для 320
        public List<FrequencyResult> ResultsB1 { get; set; } = new();
        public List<FrequencyResult> ResultsB2 { get; set; } = new();

        // Для остальных
        public List<FrequencyResult> Results { get; set; } = new();

        public class FrequencyResult
        {
            public int Number { get; set; }
            public int Count { get; set; }
            public double Chance { get; set; }
            public string Type { get; set; } = "";
        }

        public void Dispose()
        {
            Results?.Clear();
            ResultsB1?.Clear();
            ResultsB2?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A14_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

            bool isDesc = Direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (DrawCount.HasValue && DrawCount.Value > 0)
                query = query.Take(DrawCount.Value);

            var draws = await query.ToListAsync();
            if (!draws.Any()) return;

            // --- ЛОГИКА ДЛЯ 320 ---
            if (Game.Equals("320", StringComparison.OrdinalIgnoreCase))
            {
                int maxNumber = 20;

                // Подсчёт для B1
                Dictionary<int, int> countsB1 = new();
                int totalB1 = 0;

                foreach (var item in draws)
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        var prop = item.GetType().GetProperty($"B1{i}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(item);
                            if (int.TryParse(val?.ToString(), out var num) && num > 0)
                            {
                                if (!countsB1.ContainsKey(num))
                                    countsB1[num] = 1;
                                else
                                    countsB1[num]++;
                                totalB1++;
                            }
                        }
                    }
                }

                if (totalB1 > 0)
                {
                    var sortedB1 = countsB1.OrderByDescending(v => v.Value).ToList();
                    int take = Math.Min(10, sortedB1.Count);

                    // Частые
                    foreach (var item in sortedB1.Take(take))
                        ResultsB1.Add(new FrequencyResult
                        {
                            Number = item.Key,
                            Count = item.Value,
                            Chance = Math.Round(item.Value / (double)totalB1 * 100, 4),
                            Type = "Частое"
                        });

                    // Редкие
                    int start = Math.Max(0, sortedB1.Count - take);
                    foreach (var item in sortedB1.Skip(start).Take(take).OrderBy(v => v.Value))
                        ResultsB1.Add(new FrequencyResult
                        {
                            Number = item.Key,
                            Count = item.Value,
                            Chance = Math.Round(item.Value / (double)totalB1 * 100, 4),
                            Type = "Редкое"
                        });
                }

                // Подсчёт для B2
                Dictionary<int, int> countsB2 = new();
                int totalB2 = 0;

                foreach (var item in draws)
                {
                    for (int i = 1; i <= 3; i++)
                    {
                        var prop = item.GetType().GetProperty($"B2{i}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(item);
                            if (int.TryParse(val?.ToString(), out var num) && num > 0)
                            {
                                if (!countsB2.ContainsKey(num))
                                    countsB2[num] = 1;
                                else
                                    countsB2[num]++;
                                totalB2++;
                            }
                        }
                    }
                }

                if (totalB2 > 0)
                {
                    var sortedB2 = countsB2.OrderByDescending(v => v.Value).ToList();
                    int take = Math.Min(10, sortedB2.Count);

                    // Частые
                    foreach (var item in sortedB2.Take(take))
                        ResultsB2.Add(new FrequencyResult
                        {
                            Number = item.Key,
                            Count = item.Value,
                            Chance = Math.Round(item.Value / (double)totalB2 * 100, 4),
                            Type = "Частое"
                        });

                    // Редкие
                    int start = Math.Max(0, sortedB2.Count - take);
                    foreach (var item in sortedB2.Skip(start).Take(take).OrderBy(v => v.Value))
                        ResultsB2.Add(new FrequencyResult
                        {
                            Number = item.Key,
                            Count = item.Value,
                            Chance = Math.Round(item.Value / (double)totalB2 * 100, 4),
                            Type = "Редкое"
                        });
                }
            }
            // --- ОСТАЛЬНЫЕ ИГРЫ ---
            else
            {
                var first = draws.First();

                var numberProps = first.GetType().GetProperties()
                 .Where(p => p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _))
                 .OrderBy(p => int.Parse(p.Name.Substring(1)))
                 .ToList();

                Dictionary<int, int> counts = new();
                int total = 0;

                foreach (var item in draws)
                {
                    foreach (var p in numberProps)
                    {
                        var val = p.GetValue(item);
                        if (val == null) continue;

                        if (!int.TryParse(val.ToString(), out int num) || num == 0)
                            continue;

                        // Игнорируем 0 для 5/36
                        if (Game.Equals("5x36", StringComparison.OrdinalIgnoreCase) && num == 0)
                            continue;

                        if (!counts.ContainsKey(num))
                            counts[num] = 1;
                        else
                            counts[num]++;

                        total++;
                    }
                }

                if (total == 0) return;

                var sorted = counts.OrderByDescending(v => v.Value).ToList();
                int take = Math.Min(10, sorted.Count);

                // Частые
                foreach (var item in sorted.Take(take))
                    Results.Add(new FrequencyResult
                    {
                        Number = item.Key,
                        Count = item.Value,
                        Chance = Math.Round(item.Value / (double)total * 100, 4),
                        Type = "Частое"
                    });

                // Редкие
                int start = Math.Max(0, sorted.Count - take);
                foreach (var item in sorted.Skip(start).Take(take).OrderBy(v => v.Value))
                    Results.Add(new FrequencyResult
                    {
                        Number = item.Key,
                        Count = item.Value,
                        Chance = Math.Round(item.Value / (double)total * 100, 4),
                        Type = "Редкое"
                    });
            }
        }

        [BindProperty(SupportsGet = true)]
        public string? SelectedTable { get; set; }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            // Вызываем специфичный метод экспорта, передав SelectedTable
            return await _s.Csv14.ExportAsync(Game, DrawCount, Direction, Title, SelectedTable);
        }
    }
}