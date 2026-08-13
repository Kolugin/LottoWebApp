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
    public class A118Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A118Model(AppServices s)
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

        public List<BonusBallByMonthDayWeekFrequencyResult> Results { get; set; } = new();

        public class BonusBallByMonthDayWeekFrequencyResult
        {
            public string Month { get; set; } = "";
            public int DayOfMonth { get; set; }
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
            string cacheKey = $"A118_{Game}_{DrawCount}_{Direction}";
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

            var culture = new CultureInfo("ru-RU");
            var monthOrder = new Dictionary<string, int>
                {
                    {"январь", 1},
                    {"февраль", 2},
                    {"март", 3},
                    {"апрель", 4},
                    {"май", 5},
                    {"июнь", 6},
                    {"июль", 7},
                    {"август", 8},
                    {"сентябрь", 9},
                    {"октябрь", 10},
                    {"ноябрь", 11},
                    {"декабрь", 12}
                };

            var dayOfWeekOrder = new List<string> { "понедельник", "вторник", "среда", "четверг", "пятница", "суббота", "воскресенье" };
            var dayMonthWeekNumberCounts = new Dictionary<string, Dictionary<int, int>>();
            var totalDrawsPerKey = new Dictionary<string, int>();

            // Обработка данных из таблицы
            foreach (var row in draws)
            {
                // Получаем дату
                var dateValue = dateProp.GetValue(row);
                if (dateValue == null) continue;

                if (!DateTime.TryParseExact(dateValue.ToString(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue; // Пропускаем ошибочные даты
                }

                string monthName = date.ToString("MMMM", culture).ToLower();
                int dayOfMonth = date.Day;
                string dayOfWeek = date.ToString("dddd", culture).ToLower();
                var key = $"{monthName}-{dayOfMonth}-{dayOfWeek}";

                // Инициализация словарей только при необходимости
                if (!dayMonthWeekNumberCounts.TryGetValue(key, out var numberCounts))
                {
                    numberCounts = new Dictionary<int, int>();
                    dayMonthWeekNumberCounts[key] = numberCounts;
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

                // Обновление общего количества тиражей для ключа
                if (totalDrawsPerKey.TryGetValue(key, out int totalDraws))
                {
                    totalDrawsPerKey[key] = totalDraws + 1;
                }
                else
                {
                    totalDrawsPerKey[key] = 1;
                }
            }

            // Формирование результатов
            var sortedKeys = dayMonthWeekNumberCounts.Keys
                .OrderBy(key =>
                {
                    var parts = key.Split('-');
                    int monthNumber = monthOrder[parts[0]];
                    int dayOfMonth = int.Parse(parts[1]);
                    int dayOfWeekIndex = dayOfWeekOrder.IndexOf(parts[2]);
                    return monthNumber * 10000 + dayOfMonth * 100 + dayOfWeekIndex;
                })
                .ToList();

            foreach (var key in sortedKeys)
            {
                var parts = key.Split('-');
                string monthName = parts[0];
                int dayOfMonth = int.Parse(parts[1]);
                string dayOfWeek = parts[2];
                var numberCounts = dayMonthWeekNumberCounts[key];
                int totalDraws = totalDrawsPerKey[key];

                if (totalDraws == 0) continue; // Пропускаем пустые группы

                var sortedNumbers = numberCounts.OrderByDescending(x => x.Value).ToList();
                var frequentNumbers = sortedNumbers.Take(10); // В оригинальном алгоритме: первые 10 частых
                // В заголовках таблицы нет столбца "Тип", значит, выводим только частые
                foreach (var num in frequentNumbers)
                {
                    double chance = Math.Round((double)num.Value / totalDraws * 100, 4);
                    Results.Add(new BonusBallByMonthDayWeekFrequencyResult
                    {
                        Month = monthName,
                        DayOfMonth = dayOfMonth,
                        DayOfWeek = dayOfWeek,
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
            return await _s.Csv118.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}