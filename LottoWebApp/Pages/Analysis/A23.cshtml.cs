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
    public class A23Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A23Model(AppServices s)
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

        public List<PauseMapResult> Results { get; set; } = new();
        public bool HasTime { get; set; }
        public bool HasBonus { get; set; }
        public int TotalBalls { get; set; }

        public class PauseMapResult
        {
            public int Draw { get; set; }
            public string Date { get; set; } = "";
            public string? Time { get; set; } = null;
            public Dictionary<int, int> BallPauses { get; set; } = new();
            public Dictionary<int, int> BonusPauses { get; set; } = new(); // Для бонусов
        }

        public void Dispose()
        {
            Results?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A23_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

            bool isDesc = Direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw")) // новые - старые
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));           // старые - новые

            if (DrawCount.HasValue && DrawCount.Value > 0)
                query = query.Take(DrawCount.Value);

            var draws = (await query.ToListAsync())
     .OrderBy(e => e.GetType().GetProperty("Draw")!.GetValue(e)) // старые - новые
     .ToList();

            var first = draws.First();

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any()) return;

            // Определяем параметры для игры
            int totalBalls = 0;
            int ballCount = 0;
            HasTime = false;
            HasBonus = false;

            switch (Game?.ToLower())
            {
                case "keno":
                    totalBalls = 60;
                    ballCount = 20;
                    HasTime = false;
                    HasBonus = false;
                    break;
                case "blitz":
                    totalBalls = 20;
                    ballCount = 8;
                    HasTime = true;
                    HasBonus = true;
                    break;
                case "5x36":
                    totalBalls = 36;
                    ballCount = 6;
                    HasTime = false;
                    HasBonus = false;
                    break;
                case "6x49":
                    totalBalls = 49;
                    ballCount = 6;
                    HasTime = false;
                    HasBonus = false;
                    break;
                case "1224":
                    totalBalls = 24;
                    ballCount = 12;
                    HasTime = true;
                    HasBonus = false;
                    break;
                default:
                    totalBalls = 60;
                    ballCount = 20;
                    HasTime = false;
                    HasBonus = false;
                    break;
            }

            TotalBalls = totalBalls;

            int[] pauseCounters = new int[totalBalls + 1];
            int[] bonusPauseCounters = new int[5]; // для 1..4 бонусных

            var tempResults = new List<PauseMapResult>();

            foreach (var rowObj in draws)
            {
                var rowType = rowObj.GetType();
                var drawProp = rowType.GetProperty("Draw");
                var dateProp = rowType.GetProperty("Date");
                var timeProp = HasTime ? rowType.GetProperty("Time") : null;

                if (drawProp == null || dateProp == null) continue;

                var drawValue = drawProp.GetValue(rowObj);
                var dateValue = dateProp.GetValue(rowObj);

                if (drawValue == null || dateValue == null) continue;

                var draw = Convert.ToInt32(drawValue);
                var date = dateValue.ToString() ?? "";
                string? time = null;
                if (HasTime && timeProp != null)
                {
                    time = timeProp.GetValue(rowObj)?.ToString();
                }

                // Выпавшие основные шары
                var drawnBalls = new HashSet<int>();
                for (int i = 1; i <= ballCount; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(rowObj);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                        {
                            drawnBalls.Add(num);
                        }
                    }
                }

                // Выпавший бонус
                int bonusBall = 0;
                if (HasBonus)
                {
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var val = bbProp.GetValue(rowObj);
                        if (val != null && int.TryParse(val.ToString(), out int num))
                        {
                            bonusBall = num;
                        }
                    }
                }

                // Паузы по основным шарам
                var ballPauses = new Dictionary<int, int>();
                for (int ball = 1; ball <= totalBalls; ball++)
                {
                    if (drawnBalls.Contains(ball))
                        pauseCounters[ball] = 0;
                    else
                        pauseCounters[ball]++;

                    ballPauses[ball] = pauseCounters[ball];
                }

                // Паузы по бонусу (обычно 1–4)
                var bonusPauses = new Dictionary<int, int>();
                if (HasBonus)
                {
                    for (int b = 1; b <= 4; b++)
                    {
                        if (bonusBall == b)
                            bonusPauseCounters[b] = 0;
                        else
                            bonusPauseCounters[b]++;

                        bonusPauses[b] = bonusPauseCounters[b];
                    }
                }

                tempResults.Add(new PauseMapResult
                {
                    Draw = draw,
                    Date = date,
                    Time = time,
                    BallPauses = ballPauses,
                    BonusPauses = bonusPauses
                });
            }

            // Теперь инвертируем порядок, если изначально было сортировка по убыванию
            Results = isDesc ? tempResults.AsEnumerable().Reverse().ToList() : tempResults;
        }

        public async Task<IActionResult> OnPostDownloadCsv()
        {
            // Вызываем специфичный метод экспорта
            return await _s.Csv23.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}