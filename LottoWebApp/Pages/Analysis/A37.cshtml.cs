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
        public bool Is320 { get; set; }

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

            Is320 = Game.Equals("320", StringComparison.OrdinalIgnoreCase);
            var propNames320 = new[] { "B11", "B12", "B13", "B21", "B22", "B23" };

            if (Is320)
                NumberOfBalls = 6;
            else
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

                    // 1. Подсчет выпадений для каждого шара в текущем окне (от startIndex до startIndex + interval - 1)
                    for (int i = startIndex; i < startIndex + interval; i++)
                    {
                        var currentRow = draws[i];

                        if (Is320)
                        {
                            foreach (var propName in propNames320)
                            {
                                var prop = first.GetType().GetProperty(propName);
                                if (prop != null)
                                {
                                    var val = prop.GetValue(currentRow);
                                    if (val != null && int.TryParse(val.ToString(), out int ball) && ball != 0)
                                    {
                                        if (!ballCounts.ContainsKey(ball))
                                            ballCounts[ball] = 0;
                                        ballCounts[ball]++;
                                    }
                                }
                            }
                        }
                        else
                        {
                            foreach (var prop in numberProps)
                            {
                                if (prop.Name == "BB") continue;
                                var val = prop.GetValue(currentRow);
                                if (val != null && int.TryParse(val.ToString(), out int ball) && ball != 0)
                                {
                                    if (!ballCounts.ContainsKey(ball))
                                        ballCounts[ball] = 0;
                                    ballCounts[ball]++;
                                }
                            }
                        }
                    }

                    // 2. Запись вероятностей для шаров из ПЕРВОГО тиража текущего интервала (startIndex)
                    var startRow = draws[startIndex];
                    for (int position = 1; position <= NumberOfBalls; position++)
                    {
                        int ball = 0;
                        if (Is320)
                        {
                            var prop = first.GetType().GetProperty(propNames320[position - 1]);
                            if (prop != null)
                            {
                                var val = prop.GetValue(startRow);
                                if (val != null && int.TryParse(val.ToString(), out int num))
                                    ball = num;
                            }
                        }
                        else
                        {
                            var positionProp = numberProps.FirstOrDefault(p => p.Name == $"B{position}");
                            if (positionProp != null)
                            {
                                var val = positionProp.GetValue(startRow);
                                if (val != null && int.TryParse(val.ToString(), out int num))
                                    ball = num;
                            }
                        }

                        if (ball != 0 && ballCounts.ContainsKey(ball))
                        {
                            double probability = (double)interval / ballCounts[ball];
                            row.Positions[position] = probability.ToString("F8", CultureInfo.InvariantCulture);
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

        public string GetColorClassForValue(string? valueStr, int colIndex)
        {
            if (string.IsNullOrEmpty(valueStr) || valueStr == "-")
                return "table-secondary";

            if (!double.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
                return "table-light";

            // Значение - это "шаг" (через сколько тиражей выпадает).
            // 1.0 - каждый тираж (самое горячее)
            if (value <= 2.0)
                return "green-cell";
            if (value <= 5.0)
                return "yellow-cell";
            return "red-cell";
        }
    }
}