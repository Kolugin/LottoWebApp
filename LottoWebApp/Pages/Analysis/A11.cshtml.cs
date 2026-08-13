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
    public class A11Model : PageModel, IDisposable
    {
        private readonly AppServices _s;

        public A11Model(AppServices s)
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

        public List<DrawView> Draws { get; set; } = new();
        public List<string> Headers { get; set; } = new();
        public List<string> ChartLabels { get; set; } = new();
        public List<int> ChartValues { get; set; } = new();

        // НОВОЕ: Отдельные списки для 3+3
        public List<string> ChartLabels1 { get; set; } = new();
        public List<int> ChartValues1 { get; set; } = new();
        public List<string> ChartLabels2 { get; set; } = new();
        public List<int> ChartValues2 { get; set; } = new();

        public void Dispose()
        {
            Draws?.Clear();
            Headers?.Clear();
            ChartLabels?.Clear();
            ChartValues?.Clear();
            ChartLabels1?.Clear();
            ChartValues1?.Clear();
            ChartLabels2?.Clear();
            ChartValues2?.Clear();
        }

        private class DrawData
        {
            public List<DrawView> Draws { get; set; } = new();
            public List<string> Headers { get; set; } = new();
            public List<string> ChartLabels { get; set; } = new();
            public List<int> ChartValues { get; set; } = new();
            // НОВОЕ: Для 3+3
            public List<string> ChartLabels1 { get; set; } = new();
            public List<int> ChartValues1 { get; set; } = new();
            public List<string> ChartLabels2 { get; set; } = new();
            public List<int> ChartValues2 { get; set; } = new();
        }

        public async Task OnGetAsync()
        {
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            string cacheKey = $"A11_{Game}_{DrawCount}_{Direction}";

            var drawData = await _s.Cache.GetOrCreateAsync(cacheKey, async () =>
            {
                var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

                bool isDesc = Direction == "newToOld";
                query = isDesc
                    ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                    : query.OrderBy(e => EF.Property<int>(e, "Draw"));

                if (DrawCount.HasValue && DrawCount.Value > 0)
                    query = query.Take(DrawCount.Value);

                var result = await query.ToListAsync();
                if (!result.Any()) return new DrawData();

                var data = new DrawData();

                // --- СПЕЦИАЛЬНАЯ ВЕТКА ДЛЯ 3+3 ---
                if (Game.Equals("320", StringComparison.OrdinalIgnoreCase))
                {
                    int maxNumber = 20; // диапазон чисел для 3+3
                    int[] freqArray1 = new int[maxNumber + 1];
                    int[] freqArray2 = new int[maxNumber + 1];

                    data.Headers.Add("Тираж");
                    data.Headers.Add("Дата");
                    data.Headers.Add("Время");
                    data.Headers.AddRange(new[] { "B11", "B12", "B13", "B21", "B22", "B23" });

                    data.Draws.Capacity = result.Count;

                    foreach (var item in result)
                    {
                        var draw = new DrawView
                        {
                            Draw = Convert.ToInt32(item.GetType().GetProperty("Draw")?.GetValue(item)),
                            Date = Convert.ToDateTime(item.GetType().GetProperty("Date")?.GetValue(item)),
                            Time = item.GetType().GetProperty("Time")?.GetValue(item)?.ToString() ?? ""
                        };

                        // Заполняем Numbers1 (B11, B12, B13)
                        for (int i = 1; i <= 3; i++)
                        {
                            var prop = item.GetType().GetProperty($"B1{i}");
                            int num = prop != null && int.TryParse(prop.GetValue(item)?.ToString(), out var n) ? n : 0;
                            draw.Numbers1.Add(num);
                            if (num >= 1 && num <= maxNumber) freqArray1[num]++;
                        }

                        // Заполняем Numbers2 (B21, B22, B23)
                        for (int i = 1; i <= 3; i++)
                        {
                            var prop = item.GetType().GetProperty($"B2{i}");
                            int num = prop != null && int.TryParse(prop.GetValue(item)?.ToString(), out var n) ? n : 0;
                            draw.Numbers2.Add(num);
                            if (num >= 1 && num <= maxNumber) freqArray2[num]++;
                        }

                        // Объединяем в Numbers для совместимости
                        draw.Numbers.AddRange(draw.Numbers1);
                        draw.Numbers.AddRange(draw.Numbers2);

                        data.Draws.Add(draw);
                    }

                    // Формируем отдельные списки для графиков
                    for (int i = 1; i <= maxNumber; i++)
                    {
                        if (freqArray1[i] > 0)
                        {
                            data.ChartLabels1.Add(i.ToString());
                            data.ChartValues1.Add(freqArray1[i]);
                        }
                        if (freqArray2[i] > 0)
                        {
                            data.ChartLabels2.Add(i.ToString());
                            data.ChartValues2.Add(freqArray2[i]);
                        }
                    }
                }
                // --- ОСНОВНАЯ ЛОГИКА (как и раньше) ---
                else
                {
                    int maxNumber = 60;
                    int[] freqArray = new int[maxNumber + 1];
                    int[] bonusArray = new int[5];

                    bool showTime = !(Game.Equals("keno", StringComparison.OrdinalIgnoreCase)
                                     || Game.Equals("5x36", StringComparison.OrdinalIgnoreCase)
                                     || Game.Equals("6x49", StringComparison.OrdinalIgnoreCase));

                    var first = result.First();
                    var numberProps = first.GetType().GetProperties()
                        .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                        .ToList();

                    numberProps.Sort((p1, p2) =>
                    {
                        if (p1.Name == "BB") return 1;
                        if (p2.Name == "BB") return -1;
                        return int.Parse(p1.Name.Substring(1)).CompareTo(int.Parse(p2.Name.Substring(1)));
                    });

                    data.Headers.Add("Тираж");
                    data.Headers.Add("Дата");
                    if (showTime) data.Headers.Add("Время");
                    data.Headers.AddRange(numberProps.Select(p => p.Name == "BB" ? "Бонус" : "№ " + p.Name.Substring(1)));

                    data.Draws.Capacity = result.Count;

                    foreach (var item in result)
                    {
                        var draw = new DrawView
                        {
                            Draw = Convert.ToInt32(item.GetType().GetProperty("Draw")?.GetValue(item)),
                            Date = Convert.ToDateTime(item.GetType().GetProperty("Date")?.GetValue(item)),
                            Time = showTime ? item.GetType().GetProperty("Time")?.GetValue(item)?.ToString() ?? "" : ""
                        };

                        draw.Numbers.Capacity = numberProps.Count;

                        foreach (var p in numberProps)
                        {
                            var val = p.GetValue(item);
                            int num = (val != null && int.TryParse(val.ToString(), out var n)) ? n : 0;
                            draw.Numbers.Add(num);

                            if (num == 0) continue;

                            if (p.Name == "BB")
                            {
                                if (Game.Equals("blitz", StringComparison.OrdinalIgnoreCase) && num >= 1 && num <= 4)
                                    bonusArray[num]++;
                            }
                            else
                            {
                                if (num >= 1 && num <= maxNumber)
                                    freqArray[num]++;
                            }
                        }

                        data.Draws.Add(draw);
                    }

                    for (int i = 1; i <= maxNumber; i++)
                    {
                        if (freqArray[i] > 0)
                        {
                            data.ChartLabels.Add(i.ToString());
                            data.ChartValues.Add(freqArray[i]);
                        }
                    }

                    if (Game.Equals("blitz", StringComparison.OrdinalIgnoreCase))
                    {
                        for (int i = 1; i <= 4; i++)
                        {
                            data.ChartLabels.Add("Бонус " + i);
                            data.ChartValues.Add(bonusArray[i]);
                        }
                    }
                }

                return data;
            }, TimeSpan.FromMinutes(15));

            Draws = drawData.Draws;
            Headers = drawData.Headers;
            ChartLabels = drawData.ChartLabels;
            ChartValues = drawData.ChartValues;
            // НОВОЕ: Присваиваем для 3+3
            ChartLabels1 = drawData.ChartLabels1;
            ChartValues1 = drawData.ChartValues1;
            ChartLabels2 = drawData.ChartLabels2;
            ChartValues2 = drawData.ChartValues2;
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            bool enableSorting = false;
            return await _s.Csv11.ExportAsync(Game, DrawCount, Direction, enableSorting, Title);
        }
    }
}