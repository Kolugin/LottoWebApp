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
    public class A41Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A41Model(AppServices s)
        {
            _s = s;
        }

        [BindProperty(SupportsGet = true)]
        public string? Game { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? SelectedNumber { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? DrawCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Direction { get; set; }

        public BallStatisticsResult? Statistics { get; set; }
        public bool HasTime { get; set; }

        public class BallStatisticsResult
        {
            public int Number { get; set; }
            public int Occurrences { get; set; }
            public string LastOccurrenceDate { get; set; } = "";
            public double ProbabilityOverall { get; set; }
            public List<DateEntry> DateEntries { get; set; } = new();
        }

        public class DateEntry
        {
            public string Date { get; set; } = "";
            public string Time { get; set; } = "";
        }

        public void Dispose()
        {
            Statistics = null;
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A41_{Game}_{DrawCount}_{Direction}";
            if (!string.IsNullOrEmpty(Game) && SelectedNumber.HasValue && SelectedNumber.Value > 0)
            {
                if (string.IsNullOrEmpty(Direction)) Direction = "newToOld";
                await LoadStatistics();
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game) || !SelectedNumber.HasValue || SelectedNumber.Value <= 0)
            {
                return Page();
            }

            if (string.IsNullOrEmpty(Direction)) Direction = "newToOld";
            await LoadStatistics();
            return Page();
        }

        private async Task LoadStatistics()
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

            // ќпредел€ем количество шаров в зависимости от игры
            int ballCount = Game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            int totalDraws = draws.Count;
            int occurrences = 0;

            var dateList = new List<DateEntry>();
            DateTime latestDate = DateTime.MinValue;

            foreach (var rowObj in draws)
            {
                var rowType = rowObj.GetType();
                var dateProp = rowType.GetProperty("Date");
                var timeProp = (Game?.ToLower() == "blitz" || Game?.ToLower() == "1224") ? rowType.GetProperty("Time") : null;

                var drawBalls = new List<int>();
                for (int i = 1; i <= ballCount; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(rowObj);
                        if (val != null && int.TryParse(val.ToString(), out int num))
                        {
                            drawBalls.Add(num);
                        }
                    }
                }

                if (SelectedNumber.HasValue && drawBalls.Contains(SelectedNumber.Value))
                {
                    occurrences++;
                    var dateValue = dateProp?.GetValue(rowObj)?.ToString();
                    var timeValue = timeProp?.GetValue(rowObj)?.ToString();

                    if (!string.IsNullOrEmpty(dateValue))
                    {
                        dateList.Add(new DateEntry { Date = dateValue, Time = timeValue ?? "" });

                        // ѕровер€ем, новее ли текуща€ дата
                        if (DateTime.TryParseExact(dateValue, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var currentDate))
                        {
                            if (currentDate > latestDate)
                                latestDate = currentDate;
                        }
                    }
                }
            }

            double probabilityOverall = (occurrences / (double)totalDraws) * 100;

            Statistics = new BallStatisticsResult
            {
                Number = SelectedNumber ?? 0,
                Occurrences = occurrences,
                LastOccurrenceDate = latestDate != DateTime.MinValue ? latestDate.ToString("dd.MM.yyyy") : "нет данных",
                ProbabilityOverall = probabilityOverall,
                DateEntries = dateList
            };

            HasTime = Game?.ToLower() == "blitz" || Game?.ToLower() == "1224";
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            if (string.IsNullOrEmpty(Game) || !SelectedNumber.HasValue || SelectedNumber.Value <= 0)
            {
                return BadRequest("»гра и номер шара об€зательны.");
            }

            return await _s.Csv41.ExportAsync(Game, SelectedNumber.Value, DrawCount, Direction);
        }
    }
}
