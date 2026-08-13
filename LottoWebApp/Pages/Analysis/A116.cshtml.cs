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
    public class A116Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A116Model(AppServices s)
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

        public List<BonusBallByYearFrequencyResult> Results { get; set; } = new();

        public class BonusBallByYearFrequencyResult
        {
            public int Year { get; set; }
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
            string cacheKey = $"A116_{Game}_{DrawCount}_{Direction}";
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

            var yearNumberCounts = new Dictionary<int, Dictionary<int, int>>();
            var yearTotalDraws = new Dictionary<int, int>();

            foreach (var row in draws)
            {
                // Получаем дату
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue; // Пропускаем ошибочные даты
                }

                int year = date.Year;

                // Инициализация словарей только при необходимости
                if (!yearNumberCounts.TryGetValue(year, out var numberCounts))
                {
                    numberCounts = new Dictionary<int, int>();
                    yearNumberCounts[year] = numberCounts;
                }

                if (!yearTotalDraws.TryGetValue(year, out int totalDraws))
                {
                    totalDraws = 0;
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

                yearTotalDraws[year] = totalDraws + 1;
            }

            // Формирование результатов
            foreach (var year in yearNumberCounts.Keys.OrderBy(y => y))
            {
                var numberCounts = yearNumberCounts[year];
                int totalDraws = yearTotalDraws[year];

                var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                var frequentNumbers = sortedNumbers.Take(4); // В оригинальном алгоритме: первые 4 частых
                // В заголовках таблицы нет столбца "Тип", значит, выводим только частые
                foreach (var num in frequentNumbers)
                {
                    double chance = Math.Round((double)num.Value / totalDraws * 100, 4);
                    Results.Add(new BonusBallByYearFrequencyResult
                    {
                        Year = year,
                        Number = num.Key,
                        Count = num.Value,
                        Chance = chance
                    });
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            // Вызываем специфичный метод экспорта
            return await _s.Csv116.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}