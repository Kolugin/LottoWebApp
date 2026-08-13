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
    public class A63Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A63Model(AppServices s)
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
        public int MinHorizontalLineLength { get; set; } = 2;

        [BindProperty(SupportsGet = true)]
        public int MinVerticalLineLength { get; set; } = 2;

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        public List<LineResult>? Lines { get; set; }
        public int MaxBallNumber { get; set; }
        public int NumberOfBalls { get; set; }
        public bool UseBonus { get; set; }
        public bool HasTime { get; set; }
        private List<Dictionary<int, string>>? lotteryMap;

        public class LineResult
        {
            public int StartPosition { get; set; }
            public int EndPosition { get; set; }
            public int StartDraw { get; set; }
            public int EndDraw { get; set; }
            public string LineType { get; set; } = "";
            public string Numbers { get; set; } = "";
        }

        public void Dispose()
        {
            Lines?.Clear();
            lotteryMap?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A63_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            if (MinHorizontalLineLength < 2) MinHorizontalLineLength = 2;
            if (MinVerticalLineLength < 2) MinVerticalLineLength = 2;

            await FindLinesAndShapes();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            if (MinHorizontalLineLength < 2) MinHorizontalLineLength = 2;
            if (MinVerticalLineLength < 2) MinVerticalLineLength = 2;

            await FindLinesAndShapes();
            return Page();
        }

        private async Task FindLinesAndShapes()
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
            lotteryMap = new List<Dictionary<int, string>>();
            var drawProp = first.GetType().GetProperty("Draw");
            var dateProp = first.GetType().GetProperty("Date");
            var timeProp = HasTime ? first.GetType().GetProperty("Time") : null;

            foreach (var row in draws)
            {
                var rowMap = new Dictionary<int, string>();

                // Добавляем служебные поля
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

                // Заполняем числа по шарам (№1, №2, ..., №N)
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

            int rowCount = lotteryMap.Count;

            // Поиск горизонтальных линий
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                await FindLinesInRow(lotteryMap[rowIndex], rowIndex + 1, "Горизонтальная");
            }

            // Поиск вертикальных линий
            for (int ballIndex = 1; ballIndex <= MaxBallNumber; ballIndex++)
            {
                await FindLinesInColumn(ballIndex, "Вертикальная");
            }

            // Поиск пересечений (фигур)
            await FindShapes();
        }

        private async Task FindLinesInRow(Dictionary<int, string> row, int rowIndex, string lineType)
        {
            int start = -1;
            string currentLineNumbers = "";

            for (int col = 1; col <= MaxBallNumber; col++)
            {
                string cellValue = row[col];

                if (cellValue != "*")
                {
                    if (start == -1)
                    {
                        start = col;
                    }
                    currentLineNumbers += (currentLineNumbers == "" ? "" : ", ") + cellValue;
                }
                else
                {
                    int count = currentLineNumbers.Split(',').Length;
                    if (count >= (lineType == "Горизонтальная" ? MinHorizontalLineLength : MinVerticalLineLength))
                    {
                        Lines.Add(new LineResult
                        {
                            StartPosition = start,
                            EndPosition = start + count - 1,
                            StartDraw = rowIndex,
                            EndDraw = rowIndex,
                            LineType = lineType,
                            Numbers = currentLineNumbers
                        });
                    }
                    start = -1;
                    currentLineNumbers = "";
                }
            }

            int finalCount = currentLineNumbers.Split(',').Length;
            if (finalCount >= (lineType == "Горизонтальная" ? MinHorizontalLineLength : MinVerticalLineLength))
            {
                Lines.Add(new LineResult
                {
                    StartPosition = start,
                    EndPosition = start + finalCount - 1,
                    StartDraw = rowIndex,
                    EndDraw = rowIndex,
                    LineType = lineType,
                    Numbers = currentLineNumbers
                });
            }
        }

        private async Task FindLinesInColumn(int colIndex, string lineType)
        {
            int start = -1;
            string currentLineNumbers = "";

            for (int row = 0; row < lotteryMap.Count; row++)
            {
                string cellValue = lotteryMap[row][colIndex];

                if (cellValue != "*")
                {
                    if (start == -1)
                    {
                        start = row + 1;
                    }
                    currentLineNumbers += (currentLineNumbers == "" ? "" : ", ") + cellValue;
                }
                else
                {
                    int count = currentLineNumbers.Split(',').Length;
                    if (count >= (lineType == "Вертикальная" ? MinVerticalLineLength : MinHorizontalLineLength))
                    {
                        Lines.Add(new LineResult
                        {
                            StartPosition = colIndex,
                            EndPosition = colIndex,
                            StartDraw = start,
                            EndDraw = start + count - 1,
                            LineType = lineType,
                            Numbers = currentLineNumbers
                        });
                    }
                    start = -1;
                    currentLineNumbers = "";
                }
            }

            int finalCount = currentLineNumbers.Split(',').Length;
            if (finalCount >= (lineType == "Вертикальная" ? MinVerticalLineLength : MinHorizontalLineLength))
            {
                Lines.Add(new LineResult
                {
                    StartPosition = colIndex,
                    EndPosition = colIndex,
                    StartDraw = start,
                    EndDraw = start + finalCount - 1,
                    LineType = lineType,
                    Numbers = currentLineNumbers
                });
            }
        }

        private async Task FindShapes()
        {
            var horizontalLines = Lines
                .Where(line => line.LineType == "Горизонтальная")
                .ToList();

            var verticalLines = Lines
                .Where(line => line.LineType == "Вертикальная")
                .ToList();

            foreach (var h in horizontalLines)
            {
                foreach (var v in verticalLines)
                {
                    // Пересечение по координатам
                    if (h.StartDraw >= v.StartDraw && h.StartDraw <= v.EndDraw &&
                        v.StartPosition >= h.StartPosition && v.StartPosition <= h.EndPosition)
                    {
                        int row = h.StartDraw - 1; // индекс строки в lotteryMap
                        int col = v.StartPosition - 1; // индекс столбца в lotteryMap

                        var intersectValue = lotteryMap[row][col + 1]; // +1 потому что в карте ключи начинаются с 1
                        if (string.IsNullOrEmpty(intersectValue) || intersectValue == "*") continue;

                        var hNumbers = h.Numbers.Split(',').Select(n => n.Trim()).ToList();
                        var vNumbers = v.Numbers.Split(',').Select(n => n.Trim()).ToList();

                        if (hNumbers.Contains(intersectValue) && vNumbers.Contains(intersectValue))
                        {
                            string shapeInfo = $"Пересекающееся число: {intersectValue} | Горизонталь: ({h.Numbers}) | Вертикаль: ({v.Numbers})";

                            Lines.Add(new LineResult
                            {
                                StartPosition = h.StartPosition,
                                EndPosition = h.EndPosition,
                                StartDraw = h.StartDraw,
                                EndDraw = h.EndDraw,
                                LineType = "Пересечение",
                                Numbers = shapeInfo
                            });
                        }
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv63.ExportAsync(Game, DrawCount, Direction, MinHorizontalLineLength, MinVerticalLineLength, Title);
        }
    }
}