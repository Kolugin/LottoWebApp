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
    public class A115Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A115Model(AppServices s)
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

        public List<BonusBallByDayOfMonthFrequencyResult> Results { get; set; } = new();

        public class BonusBallByDayOfMonthFrequencyResult
        {
            public int DayOfMonth { get; set; }
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
            string cacheKey = $"A115_{Game}_{DrawCount}_{Direction}";
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

            var dayOfMonthNumberCounts = new Dictionary<int, Dictionary<int, int>>();
            var dayOfMonthTotalDraws = new Dictionary<int, int>();

            foreach (var row in draws)
            {
                // Получаем дату
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue; // Пропускаем ошибочные даты
                }

                int dayOfMonth = date.Day;

                // Инициализация словарей только при необходимости
                if (!dayOfMonthNumberCounts.TryGetValue(dayOfMonth, out var numberCounts))
                {
                    numberCounts = new Dictionary<int, int>();
                    dayOfMonthNumberCounts[dayOfMonth] = numberCounts;
                }

                // Получаем значение BB
                var bbValue = bonusBallProp.GetValue(row);
                if (bbValue == null || !int.TryParse(bbValue.ToString(), out int number) || number == 0) continue;

                // Обновление счетчиков
                if (numberCounts.TryGetValue(number, out int count))
                {
                    numberCounts[number] = count + 1;
                }
                else
                {
                    numberCounts[number] = 1;
                }

                // Обновление общего количества тиражей для дня месяца
                if (dayOfMonthTotalDraws.TryGetValue(dayOfMonth, out int totalDraws))
                {
                    dayOfMonthTotalDraws[dayOfMonth] = totalDraws + 1;
                }
                else
                {
                    dayOfMonthTotalDraws[dayOfMonth] = 1;
                }
            }

            // Формирование результатов
            for (int day = 1; day <= 31; day++)
            {
                if (dayOfMonthNumberCounts.TryGetValue(day, out var numberCounts))
                {
                    int totalDraws = dayOfMonthTotalDraws[day];
                    if (totalDraws == 0) continue; // Пропускаем пустые группы

                    var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                    var frequentNumbers = sortedNumbers.Take(4); // В оригинальном алгоритме: первые 4 частых
                    // В заголовках таблицы нет столбца "Тип", значит, выводим только частые
                    foreach (var num in frequentNumbers)
                    {
                        double chance = Math.Round((double)num.Value / totalDraws * 100, 4);
                        Results.Add(new BonusBallByDayOfMonthFrequencyResult
                        {
                            DayOfMonth = day,
                            Number = num.Key,
                            Count = num.Value,
                            Chance = chance
                        });
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            // Вызываем специфичный метод экспорта
            return await _s.Csv115.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}