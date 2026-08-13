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
    public class A31Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A31Model(AppServices s)
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

        public List<MapRowResult>? MapData { get; set; }
        public int MaxBallNumber { get; set; }
        public int NumberOfBalls { get; set; }
        public bool UseBonus { get; set; }
        public bool HasTime { get; set; }

        public class MapRowResult
        {
            public int Draw { get; set; }
            public string Date { get; set; } = "";
            public string? Time { get; set; }
            public List<int> BallNumbers { get; set; } = new();
            public int BonusNumber { get; set; }
        }

        public void Dispose()
        {
            MapData?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A31_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateUniversalMap();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateUniversalMap();
            return Page();
        }

        private async Task GenerateUniversalMap()
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

            // Определяем параметры для игры
            (MaxBallNumber, NumberOfBalls, UseBonus, HasTime) = Game?.ToLower() switch
            {
                "keno" => (60, 20, false, false),
                "blitz" => (20, 8, true, true),
                "5x36" => (36, 6, false, false),
                "6x49" => (49, 6, false, false),
                "1224" => (24, 12, false, true),
                _ => (60, 20, false, false)
            };

            MapData = new List<MapRowResult>();

            foreach (var row in draws)
            {
                var mapRow = new MapRowResult();

                var drawProp = first.GetType().GetProperty("Draw");
                var dateProp = first.GetType().GetProperty("Date");
                var timeProp = HasTime ? first.GetType().GetProperty("Time") : null;

                if (drawProp != null)
                    mapRow.Draw = Convert.ToInt32(drawProp.GetValue(row));
                if (dateProp != null)
                    mapRow.Date = dateProp.GetValue(row)?.ToString() ?? "";
                if (HasTime && timeProp != null)
                    mapRow.Time = timeProp.GetValue(row)?.ToString();

                // Заполняем HashSet шаров
                var drawBalls = new HashSet<int>();
                for (int i = 1; i <= NumberOfBalls; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int num) && num > 0)
                        {
                            drawBalls.Add(num);
                        }
                    }
                }
                mapRow.BallNumbers = drawBalls.OrderBy(x => x).ToList();

                if (UseBonus)
                {
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var bbVal = bbProp.GetValue(row);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int bonusBall))
                        {
                            mapRow.BonusNumber = bonusBall;
                        }
                    }
                }

                MapData.Add(mapRow);
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv31.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}