using DocumentFormat.OpenXml.Bibliography;
using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A321Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A321Model(AppServices s)
        {
            _s = s;
        }

        [BindProperty(SupportsGet = true)]
        public bool SortBalls { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Game { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? DrawCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Direction { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        public List<TransitionResult> TransitionResults { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }
        public bool HasTime { get; set; }

        public class TransitionResult
        {
            public int DrawNumber { get; set; }
            public string DrawDate { get; set; } = "";
            public string? DrawTime { get; set; }
            public int Position { get; set; }
            public string Transition { get; set; } = "";
        }

        public void Dispose()
        {
            
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A321_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await CalculateTransitionProbabilities();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await CalculateTransitionProbabilities();
            return Page();
        }
        private async Task CalculateTransitionProbabilities()
        {
            var gameLower = Game?.ToLower();
            if (gameLower == "blitz" || gameLower == "1224")
            {
                SortBalls = false;
            }
            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

            bool isDesc = Direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (DrawCount.HasValue && DrawCount.Value > 0)
                query = query.Take(DrawCount.Value);

            var draws = await query.ToListAsync();
            if (!draws.Any()) return;

            if (isDesc)
            {
                draws.Reverse();
            }

            var first = draws.First();

            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                .ToList();

            if (!numberProps.Any()) return;

            // Определяем параметры для игры
            (MaxBallNumber, NumberOfBalls) = Game?.ToLower() switch
            {
                "keno" => (60, 20),
                "blitz" => (20, 8),
                "5x36" => (36, 6),
                "6x49" => (49, 6),
                "1224" => (24, 12),
                _ => (60, 20)
            };

            HasTime = Game?.ToLower() == "blitz" || Game?.ToLower() == "1224";
            TransitionResults.Clear(); // Очистка перед новым расчетом

            var drawProp = first.GetType().GetProperty("Draw");
            var dateProp = first.GetType().GetProperty("Date");
            var timeProp = HasTime ? first.GetType().GetProperty("Time") : null;

            // Начинаем со второго тиража (i=1), так как нужен предыдущий (i-1)
            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                var previousBalls = GetBallsFromDraw(previousRow, NumberOfBalls, numberProps);
                var currentBalls = GetBallsFromDraw(currentRow, NumberOfBalls, numberProps);

                if (SortBalls)
                {
                    // Удаляем нули, сортируем, добавляем нули обратно для сохранения размера
                    previousBalls = previousBalls.Where(x => x != 0).OrderBy(x => x).ToList();
                    currentBalls = currentBalls.Where(x => x != 0).OrderBy(x => x).ToList();

                    while (previousBalls.Count < NumberOfBalls) previousBalls.Add(0);
                    while (currentBalls.Count < NumberOfBalls) currentBalls.Add(0);
                }

                // Получаем информацию о тираже
                var drawNumber = drawProp?.GetValue(currentRow)?.ToString() ?? (i + 1).ToString();
                var drawDate = dateProp?.GetValue(currentRow)?.ToString() ?? "";
                var drawTime = HasTime && timeProp != null ? timeProp.GetValue(currentRow)?.ToString() : "";
                int intDrawNumber = int.TryParse(drawNumber, out int dn) ? dn : i + 1;

                // Для каждой позиции
                for (int pos = 0; pos < NumberOfBalls; pos++)
                {
                    int prevBall = 0;
                    int currBall = 0;

                    if (pos < previousBalls.Count && pos < currentBalls.Count)
                    {
                        prevBall = previousBalls[pos];
                        currBall = currentBalls[pos];
                    }
                    TransitionResults.Add(new TransitionResult
                    {
                        DrawNumber = intDrawNumber,
                        DrawDate = drawDate,
                        DrawTime = drawTime,
                        Position = pos,
                        Transition = $"{prevBall} -> {currBall}"
                    });
                }
            }
        }

        private List<int> GetBallsFromDraw(object draw, int ballCount, List<PropertyInfo> numberProps)
        {
            var balls = new List<int>();

            for (int j = 1; j <= ballCount; j++)
            {
                // Ищем свойство B1, B2, ... или BB
                var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}" || (j == ballCount && p.Name == "BB"));

                int ballValue = 0;
                if (prop != null)
                {
                    var val = prop.GetValue(draw);
                    if (val != null && int.TryParse(val.ToString(), out ballValue))
                    {
                        // Значение уже в ballValue
                    }
                }
                balls.Add(ballValue);
            }
            return balls;
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv321.ExportAsync(Game, DrawCount, Direction, Title, SortBalls);
        }
    }
}