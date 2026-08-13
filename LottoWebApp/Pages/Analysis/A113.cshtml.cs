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
    public class A113Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A113Model(AppServices s)
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

        public List<BonusBallByWeekdayFrequencyResult> Results { get; set; } = new();

        public class BonusBallByWeekdayFrequencyResult
        {
            public string DayOfWeek { get; set; } = "";
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
            string cacheKey = $"A113_{Game}_{DrawCount}_{Direction}";
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
            var dateProp = first.GetType().GetProperty("Date");
            if (dateProp == null) return;

            var bonusBallProp = first.GetType().GetProperty("BB");
            if (bonusBallProp == null) return;

            // Кэшируем культуру и маппинг дней недели
            var russianCulture = new CultureInfo("ru-RU");
            var dayOfWeekNumberCounts = new Dictionary<string, Dictionary<int, int>>();

            foreach (var row in draws)
            {
                // Получаем дату
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                // Парсим дату из строки
                if (!DateTime.TryParse(dateValue.ToString(), out DateTime date))
                    continue;

                // Получаем название дня недели
                string dayOfWeek = date.ToString("dddd", russianCulture).ToLower();

                // Пропускаем некорректные значения
                var bbValue = bonusBallProp.GetValue(row);
                if (bbValue == null || !int.TryParse(bbValue.ToString(), out int number) || number == 0)
                    continue;

                // Инициализация словаря для дня недели
                if (!dayOfWeekNumberCounts.TryGetValue(dayOfWeek, out var numberCounts))
                {
                    numberCounts = new Dictionary<int, int>();
                    dayOfWeekNumberCounts[dayOfWeek] = numberCounts;
                }

                // Обновление счетчиков (оптимизированная версия)
                numberCounts[number] = numberCounts.TryGetValue(number, out int count) ? count + 1 : 1;
            }

            // Формирование результатов
            foreach (var dayEntry in dayOfWeekNumberCounts)
            {
                string currentDayOfWeek = dayEntry.Key;
                var numberCounts = dayEntry.Value;
                int totalDraws = numberCounts.Values.Sum();

                // Сортировка по убыванию количества выпадений
                foreach (var numEntry in numberCounts.OrderByDescending(nc => nc.Value))
                {
                    double chance = Math.Round((double)numEntry.Value / totalDraws * 100, 4);
                    Results.Add(new BonusBallByWeekdayFrequencyResult
                    {
                        DayOfWeek = currentDayOfWeek,
                        Number = numEntry.Key,
                        Count = numEntry.Value,
                        Chance = chance
                    });
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            // Вызываем специфичный метод экспорта
            return await _s.Csv113.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}