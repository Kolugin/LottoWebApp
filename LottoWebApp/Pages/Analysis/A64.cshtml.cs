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
    public class A64Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A64Model(AppServices s)
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
        public int MinDiagonalLineLength { get; set; } = 2;

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
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A64_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            if (MinHorizontalLineLength < 2) MinHorizontalLineLength = 2;
            if (MinVerticalLineLength < 2) MinVerticalLineLength = 2;
            if (MinDiagonalLineLength < 2) MinDiagonalLineLength = 2;

            await FindLinesShapesAndDiagonal();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            if (MinHorizontalLineLength < 2) MinHorizontalLineLength = 2;
            if (MinVerticalLineLength < 2) MinVerticalLineLength = 2;
            if (MinDiagonalLineLength < 2) MinDiagonalLineLength = 2;

            await FindLinesShapesAndDiagonal();
            return Page();
        }

        private async Task FindLinesShapesAndDiagonal()
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
            this.lotteryMap = new List<Dictionary<int, string>>();
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

            // Поиск диагональных линий (слева направо и справа налево)
            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 1; col <= MaxBallNumber; col++)
                {
                    await FindDiagonalLines(row, col, true);
                    await FindDiagonalLines(row, col, false);
                }
            }

            // Поиск фигур (пересечений)
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
                    if (currentLineNumbers.Split(',').Length >= (lineType == "Горизонтальная" ? MinHorizontalLineLength : MinVerticalLineLength))
                    {
                        Lines.Add(new LineResult
                        {
                            StartPosition = start,
                            EndPosition = start + currentLineNumbers.Split(',').Length - 1,
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

            if (currentLineNumbers.Split(',').Length >= (lineType == "Горизонтальная" ? MinHorizontalLineLength : MinVerticalLineLength))
            {
                Lines.Add(new LineResult
                {
                    StartPosition = start,
                    EndPosition = start + currentLineNumbers.Split(',').Length - 1,
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
                    if (currentLineNumbers.Split(',').Length >= (lineType == "Вертикальная" ? MinVerticalLineLength : MinHorizontalLineLength))
                    {
                        Lines.Add(new LineResult
                        {
                            StartPosition = colIndex,
                            EndPosition = colIndex,
                            StartDraw = start,
                            EndDraw = start + currentLineNumbers.Split(',').Length - 1,
                            LineType = lineType,
                            Numbers = currentLineNumbers
                        });
                    }
                    start = -1;
                    currentLineNumbers = "";
                }
            }

            if (currentLineNumbers.Split(',').Length >= (lineType == "Вертикальная" ? MinVerticalLineLength : MinHorizontalLineLength))
            {
                Lines.Add(new LineResult
                {
                    StartPosition = colIndex,
                    EndPosition = colIndex,
                    StartDraw = start,
                    EndDraw = start + currentLineNumbers.Split(',').Length - 1,
                    LineType = lineType,
                    Numbers = currentLineNumbers
                });
            }
        }

        private async Task FindDiagonalLines(int startRow, int startCol, bool leftToRight)
        {
            string currentLineNumbers = "";
            int currentRow = startRow;
            int currentCol = startCol;

            while (currentRow < lotteryMap.Count && currentCol >= 1 && currentCol <= MaxBallNumber)
            {
                string cellValue = lotteryMap[currentRow][currentCol];

                if (cellValue != "*")
                {
                    currentLineNumbers += (currentLineNumbers == "" ? "" : ", ") + cellValue;
                }
                else
                {
                    if (currentLineNumbers.Split(',').Length >= MinDiagonalLineLength)
                    {
                        Lines.Add(new LineResult
                        {
                            StartPosition = startCol,
                            EndPosition = leftToRight ? currentCol : currentCol + 2, // +1 чтобы вернуться на последнюю валидную, ещё +1 потому что индексация с 0
                            StartDraw = startRow + 1,
                            EndDraw = currentRow,
                            LineType = leftToRight ? "Диагональ слева направо" : "Диагональ справа налево",
                            Numbers = currentLineNumbers
                        });
                    }
                    currentLineNumbers = "";
                }

                currentRow++;
                currentCol += (leftToRight ? 1 : -1);
            }

            if (currentLineNumbers.Split(',').Length >= MinDiagonalLineLength)
            {
                Lines.Add(new LineResult
                {
                    StartPosition = startCol,
                    EndPosition = leftToRight ? currentCol : currentCol + 2, // +1 чтобы вернуться на последнюю валидную, ещё +1 потому что индексация с 0
                    StartDraw = startRow + 1,
                    EndDraw = currentRow,
                    LineType = leftToRight ? "Диагональ слева направо" : "Диагональ справа налево",
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

            var diagonalLinesLeftToRight = Lines
                .Where(line => line.LineType == "Диагональ слева направо")
                .ToList();

            var diagonalLinesRightToLeft = Lines
                .Where(line => line.LineType == "Диагональ справа налево")
                .ToList();

            // Пересечение Горизонтальная + Вертикальная
            foreach (var h in horizontalLines)
            {
                foreach (var v in verticalLines)
                {
                    if (v.StartPosition >= h.StartPosition && v.StartPosition <= h.EndPosition &&
                        h.StartDraw >= v.StartDraw && h.StartDraw <= v.EndDraw)
                    {
                        int rowOffset = h.StartDraw - v.StartDraw;
                        int colOffset = v.StartPosition - h.StartPosition;

                        if (rowOffset < v.Numbers.Split(',').Length && colOffset < h.Numbers.Split(',').Length)
                        {
                            string valH = h.Numbers.Split(',')[colOffset].Trim();
                            string valV = v.Numbers.Split(',')[rowOffset].Trim();

                            if (valH == valV)
                            {
                                Lines.Add(new LineResult
                                {
                                    StartPosition = h.StartPosition,
                                    EndPosition = h.EndPosition,
                                    StartDraw = h.StartDraw,
                                    EndDraw = h.EndDraw,
                                    LineType = "Пересечение (Горизонтальная + Вертикальная)",
                                    Numbers = $"Пересекающееся число: {valH} | Горизонталь: ({h.Numbers}) | Вертикаль: ({v.Numbers})"
                                });
                            }
                        }
                    }
                }
            }

            // Пересечение Вертикаль + Диагональ
            foreach (var v in verticalLines)
            {
                foreach (var d in diagonalLinesLeftToRight.Concat(diagonalLinesRightToLeft))
                {
                    if (v.StartPosition >= d.StartPosition && v.StartPosition <= d.EndPosition &&
                        d.StartDraw >= v.StartDraw && d.StartDraw <= v.EndDraw)
                    {
                        int rowOffset = d.StartDraw - v.StartDraw;
                        int diagOffset = v.StartPosition - d.StartPosition;

                        if (rowOffset < v.Numbers.Split(',').Length && diagOffset < d.Numbers.Split(',').Length && rowOffset >= 0 && diagOffset >= 0)
                        {
                            string valV = v.Numbers.Split(',')[rowOffset].Trim();
                            string valD = d.Numbers.Split(',')[diagOffset].Trim();

                            if (valV == valD)
                            {
                                Lines.Add(new LineResult
                                {
                                    StartPosition = v.StartPosition,
                                    EndPosition = v.EndPosition,
                                    StartDraw = v.StartDraw,
                                    EndDraw = v.EndDraw,
                                    LineType = "Пересечение (Вертикаль + Диагональ)",
                                    Numbers = $"Пересекающееся число: {valV} | Вертикаль: ({v.Numbers}) | Диагональ: ({d.Numbers})"
                                });
                            }
                        }
                    }
                }
            }

            // Пересечение Горизонталь + Диагональ
            foreach (var h in horizontalLines)
            {
                foreach (var d in diagonalLinesLeftToRight.Concat(diagonalLinesRightToLeft))
                {
                    if (h.StartDraw >= d.StartDraw && h.StartDraw <= d.EndDraw &&
                        d.StartPosition >= h.StartPosition && d.StartPosition <= h.EndPosition)
                    {
                        int colOffset = d.StartPosition - h.StartPosition;
                        int diagOffset = h.StartDraw - d.StartDraw;

                        if (colOffset < h.Numbers.Split(',').Length && diagOffset < d.Numbers.Split(',').Length && colOffset >= 0 && diagOffset >= 0)
                        {
                            string valH = h.Numbers.Split(',')[colOffset].Trim();
                            string valD = d.Numbers.Split(',')[diagOffset].Trim();

                            if (valH == valD)
                            {
                                Lines.Add(new LineResult
                                {
                                    StartPosition = h.StartPosition,
                                    EndPosition = h.EndPosition,
                                    StartDraw = h.StartDraw,
                                    EndDraw = h.EndDraw,
                                    LineType = "Пересечение (Горизонталь + Диагональ)",
                                    Numbers = $"Пересекающееся число: {valH} | Горизонталь: ({h.Numbers}) | Диагональ: ({d.Numbers})"
                                });
                            }
                        }
                    }
                }
            }

            // Пересечение Диагональ + Диагональ
            foreach (var d1 in diagonalLinesLeftToRight)
            {
                foreach (var d2 in diagonalLinesRightToLeft)
                {
                    var inter = FindIntersectionPoint(d1, d2);
                    if (inter != null)
                    {
                        int offset1 = inter.Value.Row - d1.StartDraw;
                        int offset2 = inter.Value.Row - d2.StartDraw;

                        if (offset1 >= 0 && offset1 < d1.Numbers.Split(',').Length &&
                            offset2 >= 0 && offset2 < d2.Numbers.Split(',').Length)
                        {
                            string val1 = d1.Numbers.Split(',')[offset1].Trim();
                            string val2 = d2.Numbers.Split(',')[offset2].Trim();

                            if (val1 == val2)
                            {
                                Lines.Add(new LineResult
                                {
                                    StartPosition = d1.StartPosition,
                                    EndPosition = d1.EndPosition,
                                    StartDraw = d1.StartDraw,
                                    EndDraw = d1.EndDraw,
                                    LineType = "Пересечение (Диагональ + Диагональ)",
                                    Numbers = $"Пересекающееся число: {val1} | Диагональ 1: ({d1.Numbers}) | Диагональ 2: ({d2.Numbers})"
                                });
                            }
                        }
                    }
                }
            }
        }

        private (int Row, int Col)? FindIntersectionPoint(LineResult dLine1, LineResult dLine2)
        {
            // Уравнения диагоналей:
            // Для dLine1 (слева направо): Row - StartRow = Col - StartCol
            // Для dLine2 (справа налево): Row - StartRow = -(Col - StartCol)

            // Решаем систему уравнений для нахождения точки пересечения
            int A1 = 1, B1 = -1, C1 = dLine1.StartDraw - dLine1.StartPosition;
            int A2 = 1, B2 = 1, C2 = dLine2.StartDraw + dLine2.StartPosition;

            int determinant = A1 * B2 - A2 * B1;

            if (determinant == 0)
            {
                // Диагонали параллельны, пересечения нет
                return null;
            }

            int row = (B2 * C1 - B1 * C2) / determinant;
            int col = (A1 * C2 - A2 * C1) / determinant;

            // Проверяем, что точка пересечения находится в пределах обеих диагоналей
            if (row >= dLine1.StartDraw && row <= dLine1.EndDraw &&
                row >= dLine2.StartDraw && row <= dLine2.EndDraw &&
                col >= dLine1.StartPosition && col <= dLine1.EndPosition &&
                col >= dLine2.StartPosition && col <= dLine2.EndPosition)
            {
                return (row, col);
            }

            return null;
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv64.ExportAsync(Game, DrawCount, Direction, MinHorizontalLineLength, MinVerticalLineLength, MinDiagonalLineLength, Title);
        }
    }
}