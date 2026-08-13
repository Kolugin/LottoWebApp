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
    public class A37Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A37Model(AppServices s)
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

        public Dictionary<int, List<IntervalRowResult>> IntervalTables { get; set; } = new();
        public int NumberOfBalls { get; set; }

        public class IntervalRowResult
        {
            public string TicketRange { get; set; } = "";
            public Dictionary<int, string> Positions { get; set; } = new();
        }

        public void Dispose()
        {
            IntervalTables.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A37_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateProbabilityDistributionTables();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateProbabilityDistributionTables();
            return Page();
        }

        private async Task GenerateProbabilityDistributionTables()
        {
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

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any()) return;

            // Определяем количество шаров для игры
            NumberOfBalls = Game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            var intervals = new List<int> { 10, 50, 100 };
            foreach (int interval in intervals)
            {
                IntervalTables[interval] = new List<IntervalRowResult>();
            }

            var ballCounts = new Dictionary<int, int>();

            foreach (int interval in intervals)
            {
                for (int startIndex = 0; startIndex <= draws.Count - interval; startIndex++)
                {
                    ballCounts.Clear();
                    var row = new IntervalRowResult();
                    row.TicketRange = $"{startIndex + 1}_{startIndex + interval}";

                    // Подсчет выпадений для каждого шара в текущем окне
                    for (int i = startIndex; i < startIndex + interval; i++)
                    {
                        var currentRow = draws[i];
                        for (int colIndex = 0; colIndex < numberProps.Count; colIndex++)
                        {
                            var prop = numberProps[colIndex];
                            var val = prop.GetValue(currentRow);
                            if (val != null && int.TryParse(val.ToString(), out int ball) && ball != 0)
                            {
                                if (!ballCounts.ContainsKey(ball))
                                    ballCounts[ball] = 0;
                                ballCounts[ball]++;
                            }
                        }
                    }

                    // Запись вероятностей для каждого шара в текущем интервале
                    for (int position = 1; position <= NumberOfBalls; position++)
                    {
                        if (startIndex + position - 1 < draws.Count)
                        {
                            var currentDraw = draws[startIndex + position - 1];
                            var positionProp = numberProps.FirstOrDefault(p => p.Name == $"B{position}");
                            if (positionProp != null)
                            {
                                var val = positionProp.GetValue(currentDraw);
                                if (val != null && int.TryParse(val.ToString(), out int ball))
                                {
                                    if (ballCounts.ContainsKey(ball))
                                    {
                                        double probability = (double)interval / ballCounts[ball];
                                        row.Positions[position] = probability.ToString("F8");
                                    }
                                    else
                                    {
                                        row.Positions[position] = "-";
                                    }
                                }
                            }
                        }
                        else
                        {
                            row.Positions[position] = "-";
                        }
                    }

                    IntervalTables[interval].Add(row);
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv37.ExportAsync(Game, DrawCount, Direction, Title);
        }

        public async Task<IActionResult> OnPostDownloadChartAsync(int interval)
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            if (!IntervalTables.ContainsKey(interval))
                return NotFound("Интервал не найден.");

            // Логика создания изображения графика для указанного интервала
            // Возвращаем файл с изображением
            var bytes = new byte[0]; 
            return new FileContentResult(bytes, "image/png") { FileDownloadName = $"A37_frequency_distribution_{interval}.png" };
        }

        public string GetColorClassForValue(string? valueStr, int colIndex)
        {
            if (string.IsNullOrEmpty(valueStr) || valueStr == "-")
                return "table-secondary";

            if (!double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                return "table-light";

            // Нормируем диапазоны под динамическое окрашивание
            double greenThreshold = 0.8;
            double yellowThreshold = 0.4;

            if (value >= greenThreshold)
                return "green-cell";   // высокая частота
            if (value >= yellowThreshold)
                return "yellow-cell";  // средняя частота
            return "red-cell";         // низкая частота
        }

    }
}