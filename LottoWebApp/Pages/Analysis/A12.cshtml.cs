using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A12Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A12Model(AppServices s)
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

        public void Dispose()
        {
            Draws?.Clear();
            ChartLabels?.Clear();
            ChartValues?.Clear();
            Headers?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A12_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

            bool isDesc = Direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (DrawCount.HasValue && DrawCount.Value > 0)
                query = query.Take(DrawCount.Value);

            var result = await query.ToListAsync();
            if (!result.Any()) return;

            Dispose();

            bool showTime = !(Game == "keno" || Game == "5x36" || Game == "6x49");

            var first = result.First();

            // Определяем все номера + бонус
            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            Headers.Add("Тираж");
            Headers.Add("Дата");
            if (showTime)
                Headers.Add("Время");

            foreach (var p in numberProps)
            {
                if (p.Name == "BB")
                    Headers.Add("BB");
                else
                    Headers.Add("№" + p.Name.Substring(1));
            }

            int maxNumber = 80;
            int[] freqArray = new int[maxNumber + 1];
            int[] bonusArray = new int[5];

            foreach (var item in result)
            {
                var draw = new DrawView
                {
                    Draw = Convert.ToInt32(item.GetType().GetProperty("Draw")?.GetValue(item)),
                    Date = Convert.ToDateTime(item.GetType().GetProperty("Date")?.GetValue(item)),
                    Time = showTime ? item.GetType().GetProperty("Time")?.GetValue(item)?.ToString() ?? "" : ""
                };

                List<int> nums = new();
                int? bonus = null;

                foreach (var p in numberProps)
                {
                    var val = p.GetValue(item);
                    if (val == null) continue;

                    if (p.Name == "BB")
                    {
                        if (int.TryParse(val.ToString(), out int b))
                            bonus = b;
                    }
                    else
                    {
                        if (int.TryParse(val.ToString(), out int n))
                            nums.Add(n);
                    }
                }

                nums = nums.Where(n => n > 0).OrderBy(n => n).ToList(); // только реальные номера

                bool hasBonus = bonus.HasValue;
                int bonusValue = bonus ?? -1;

                // Количество позиций для обычных номеров (без бонуса)
                int positionCount = numberProps.Count(p => p.Name != "BB");

                // Добавляем "-" (маркер = -1) в конец обычных номеров
                while (nums.Count < positionCount)
                    nums.Add(-1);

                // Если бонус есть — добавляем его последним элементом
                if (hasBonus)
                    nums.Add(bonusValue);

                draw.Numbers = nums;

                Draws.Add(draw);

                foreach (var n in nums)
                    if (n >= 1 && n <= maxNumber)
                        freqArray[n]++;

                if (bonus.HasValue && Game == "blitz" && bonus.Value >= 1 && bonus.Value <= 4)
                    bonusArray[bonus.Value]++;
            }

            for (int i = 1; i <= maxNumber; i++)
                if (freqArray[i] > 0)
                {
                    ChartLabels.Add(i.ToString());
                    ChartValues.Add(freqArray[i]);
                }

            if (Game == "blitz")
                for (int i = 1; i <= 4; i++)
                {
                    ChartLabels.Add("Бонус " + i);
                    ChartValues.Add(bonusArray[i]);
                }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            // Вызываем специфичный метод экспорта
            return await _s.Csv12.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}
