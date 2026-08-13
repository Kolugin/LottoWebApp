using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A112Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A112Model(AppServices s)
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

        public List<BonusBallFrequencyResult> Results { get; set; } = new();

        public class BonusBallFrequencyResult
        {
            public int Number { get; set; }
            public int Count { get; set; }
            public double Chance { get; set; }
        }

        public void Dispose()
        {
            Results?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A112_{Game}_{DrawCount}_{Direction}";
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

            var first = draws.First();

            // Ищем свойство BB (дополнительный шар)
            var bonusBallProp = first.GetType().GetProperty("BB");
            if (bonusBallProp == null) return;

            var numberCounts = new Dictionary<int, int>();

            // Обработка данных
            foreach (var row in draws)
            {
                var val = bonusBallProp.GetValue(row);
                if (val == null) continue;

                if (int.TryParse(val.ToString(), out int number) && number != 0)
                {
                    if (numberCounts.TryGetValue(number, out int count))
                    {
                        numberCounts[number] = count + 1;
                    }
                    else
                    {
                        numberCounts.Add(number, 1);
                    }
                }
            }

            // Получаем общее количество выпадений
            int totalDraws = numberCounts.Values.Sum();
            var sortedNumbers = numberCounts.OrderByDescending(n => n.Value).ToList();
            // В оригинальном алгоритме: первые 4 частых + последние 4 редких
            // Но в заголовках таблицы нет столбца "Тип", значит, выводим все
            // Оставим логику как в оригинале, но добавим все отсортированные числа
            foreach (var number in sortedNumbers)
            {
                double chance = Math.Round((double)number.Value / totalDraws * 100, 4);
                Results.Add(new BonusBallFrequencyResult
                {
                    Number = number.Key,
                    Count = number.Value,
                    Chance = chance
                });
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            // Вызываем специфичный метод экспорта
            return await _s.Csv112.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}