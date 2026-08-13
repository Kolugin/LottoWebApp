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
    public class A61Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A61Model(AppServices s)
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
        public int MinLineLength { get; set; } = 2;

        [BindProperty(SupportsGet = true)]
        public string? LineType { get; set; } = "horizontal";

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        public List<LineResult>? Lines { get; set; }
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }
        public bool UseBonus { get; set; }
        public bool HasTime { get; set; }

        public class LineResult
        {
            public int DrawNumber { get; set; }
            public int StartPosition { get; set; }
            public int EndPosition { get; set; }
            public int PositionNumber { get; set; }
            public int StartDraw { get; set; }
            public int EndDraw { get; set; }
            public string Numbers { get; set; } = "";
        }

        public void Dispose()
        {
            Lines?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A61_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            if (MinLineLength < 2) MinLineLength = 2;
            if (string.IsNullOrEmpty(LineType)) LineType = "horizontal";

            await FindLines();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            if (MinLineLength < 2) MinLineLength = 2;
            if (string.IsNullOrEmpty(LineType)) LineType = "horizontal";

            await FindLines();
            return Page();
        }

        private async Task FindLines()
        {
            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

            bool isDesc = Direction == "newToOld";
            query = isDesc
                ? query.OrderBy(e => EF.Property<int>(e, "Draw")) // Инвертируем: сначала новые = по возрастанию
                : query.OrderByDescending(e => EF.Property<int>(e, "Draw")); // сначала старые = по убыванию

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

            // Создаем карту лотереи: тираж -> [шар -> позиция или '*']
            var lotteryMap = new List<Dictionary<int, string>>();
            var drawProp = first.GetType().GetProperty("Draw");
            var dateProp = first.GetType().GetProperty("Date");
            var timeProp = HasTime ? first.GetType().GetProperty("Time") : null;

            foreach (var row in draws)
            {
                var rowMap = new Dictionary<int, string>();
                if (drawProp != null)
                {
                    rowMap[-1] = drawProp.GetValue(row)?.ToString() ?? ""; // Для номера тиража
                }
                if (dateProp != null)
                {
                    rowMap[-2] = dateProp.GetValue(row)?.ToString() ?? ""; // Для даты
                }
                if (HasTime && timeProp != null)
                {
                    rowMap[-3] = timeProp.GetValue(row)?.ToString() ?? ""; // Для времени
                }

                // Заполняем числа
                for (int ball = 1; ball <= MaxBallNumber; ball++)
                {
                    string value = "*";
                    for (int i = 1; i <= NumberOfBalls; i++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(row);
                            if (val != null && int.TryParse(val.ToString(), out int num) && num == ball)
                            {
                                value = ball.ToString();
                                break;
                            }
                        }
                    }
                    rowMap[ball] = value;
                }

                // Если есть бонус
                if (UseBonus)
                {
                    for (int bonusIndex = 1; bonusIndex <= 4; bonusIndex++)
                    {
                        rowMap[-10 - bonusIndex] = "*"; // Используем отрицательные ключи для бонусов
                    }
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var bbVal = bbProp.GetValue(row);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int bonusBall) && bonusBall >= 1 && bonusBall <= 4)
                        {
                            rowMap[-10 - bonusBall] = bonusBall.ToString();
                        }
                    }
                }

                lotteryMap.Add(rowMap);
            }

            Lines = new List<LineResult>();

            if (LineType == "horizontal")
            {
                // Поиск горизонтальных линий
                for (int rowIndex = 0; rowIndex < lotteryMap.Count; rowIndex++)
                {
                    var row = lotteryMap[rowIndex];
                    int start = -1;
                    var currentLineValues = new List<string>();

                    for (int ball = 1; ball <= MaxBallNumber; ball++)
                    {
                        string cellValue = row[ball];

                        if (cellValue != "*")
                        {
                            if (start == -1)
                                start = ball; // Начальная позиция - это номер шара

                            currentLineValues.Add(cellValue);
                        }
                        else
                        {
                            if (currentLineValues.Count >= MinLineLength)
                            {
                                Lines.Add(new LineResult
                                {
                                    DrawNumber = rowIndex + 1,
                                    StartPosition = start,
                                    EndPosition = start + currentLineValues.Count - 1,
                                    Numbers = string.Join(", ", currentLineValues)
                                });
                            }
                            start = -1;
                            currentLineValues.Clear();
                        }
                    }

                    // Проверка на конец строки
                    if (currentLineValues.Count >= MinLineLength)
                    {
                        Lines.Add(new LineResult
                        {
                            DrawNumber = rowIndex + 1,
                            StartPosition = start,
                            EndPosition = start + currentLineValues.Count - 1,
                            Numbers = string.Join(", ", currentLineValues)
                        });
                    }
                }
            }
            else
            {
                // Поиск вертикальных линий
                for (int ballIndex = 1; ballIndex <= MaxBallNumber; ballIndex++)
                {
                    int start = -1;
                    var currentLineValues = new List<string>();

                    for (int row = 0; row < lotteryMap.Count; row++)
                    {
                        string cellValue = lotteryMap[row][ballIndex];

                        if (cellValue != "*")
                        {
                            if (start == -1)
                                start = row + 1; // Начальный тираж

                            currentLineValues.Add(cellValue);
                        }
                        else
                        {
                            if (currentLineValues.Count >= MinLineLength)
                            {
                                Lines.Add(new LineResult
                                {
                                    PositionNumber = ballIndex,
                                    StartDraw = start,
                                    EndDraw = start + currentLineValues.Count - 1,
                                    Numbers = string.Join(", ", currentLineValues)
                                });
                            }
                            start = -1;
                            currentLineValues.Clear();
                        }
                    }

                    // Проверка на конец столбца
                    if (currentLineValues.Count >= MinLineLength)
                    {
                        Lines.Add(new LineResult
                        {
                            PositionNumber = ballIndex,
                            StartDraw = start,
                            EndDraw = start + currentLineValues.Count - 1,
                            Numbers = string.Join(", ", currentLineValues)
                        });
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv61.ExportAsync(Game, DrawCount, Direction, MinLineLength, LineType, Title);
        }
    }
}