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
    public class A51Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A51Model(AppServices s)
        {
            _s = s;
        }

        [BindProperty(SupportsGet = true)]
        public string? Game { get; set; }

        [BindProperty] 
        public int SelectedPosition { get; set; } = -1;

        [BindProperty(SupportsGet = true)]
        public int? DrawCount { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Direction { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool SortBalls { get; set; }

        public int[,,]? TransitionMatrixByPosition { get; set; }
        public Dictionary<(int, int), int> GeneralTransitionMatrix { get; set; } = new();
        public List<Dictionary<(int, int), int>> PositionTransitionMatrices { get; set; } = new();
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }

        public void Dispose()
        {
            GeneralTransitionMatrix.Clear();
            PositionTransitionMatrices.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A51_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game)) return;

            if (string.IsNullOrEmpty(Direction)) Direction = "newToOld";
            await CalculateTransitionProbabilities();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game)) return Page();

            if (string.IsNullOrEmpty(Direction)) Direction = "newToOld";
            await CalculateTransitionProbabilities();
            return Page();
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game)) return BadRequest("Игра обязательна.");

            if (string.IsNullOrEmpty(Direction)) Direction = "newToOld";

            return await _s.Csv51.ExportAsync(Game, DrawCount, Direction, SortBalls, SelectedPosition);
        }

        private async Task CalculateTransitionProbabilities()
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

            // Определяем количество шаров и максимальное число в зависимости от игры
            (int ballCount, int maxBallNumber) = Game?.ToLower() switch
            {
                "keno" => (20, 60),
                "blitz" => (8, 20),
                "5x36" => (6, 36),
                "6x49" => (6, 49),
                "1224" => (12, 24),
                _ => (20, 60)
            };

            NumberOfBalls = ballCount;
            MaxBallNumber = maxBallNumber;

            // Инициализируем матрицы
            TransitionMatrixByPosition = new int[maxBallNumber, maxBallNumber, ballCount];
            PositionTransitionMatrices.Clear();
            for (int i = 0; i < ballCount; i++)
            {
                PositionTransitionMatrices.Add(new Dictionary<(int, int), int>());
            }

            bool isOrderedGame = (Game?.ToLower() == "blitz" || Game?.ToLower() == "1224");

            if (isOrderedGame)
            {
                await CalculateForOrderedGames(draws, numberProps, ballCount, maxBallNumber);
            }
            else
            {
                await CalculateForUnorderedGames(draws, numberProps, ballCount, maxBallNumber, SortBalls);
            }
        }

        private async Task CalculateForOrderedGames(List<object> draws, List<System.Reflection.PropertyInfo> numberProps, int ballCount, int maxBallNumber)
        {
            var currentBalls = new List<int>();
            var previousBalls = new List<int>();

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                currentBalls.Clear();
                previousBalls.Clear();

                for (int j = 1; j <= ballCount; j++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(currentRow);
                        var valPrev = prop.GetValue(previousRow);
                        if (val != null && int.TryParse(val.ToString(), out int currNum))
                        {
                            currentBalls.Add(currNum);
                        }
                        else
                        {
                            currentBalls.Add(0);
                        }

                        if (valPrev != null && int.TryParse(valPrev.ToString(), out int prevNum))
                        {
                            previousBalls.Add(prevNum);
                        }
                        else
                        {
                            previousBalls.Add(0);
                        }
                    }
                    else
                    {
                        currentBalls.Add(0);
                        previousBalls.Add(0);
                    }
                }

                for (int pos = 0; pos < ballCount; pos++)
                {
                    int curr = currentBalls[pos];
                    int prev = previousBalls[pos];

                    if (prev > 0 && prev <= maxBallNumber && curr > 0 && curr <= maxBallNumber)
                    {
                        // Общая матрица
                        var generalKey = (prev, curr);
                        if (GeneralTransitionMatrix.ContainsKey(generalKey))
                            GeneralTransitionMatrix[generalKey]++;
                        else
                            GeneralTransitionMatrix[generalKey] = 1;

                        // Матрица по позиции
                        var posKey = (prev, curr);
                        if (PositionTransitionMatrices[pos].ContainsKey(posKey))
                            PositionTransitionMatrices[pos][posKey]++;
                        else
                            PositionTransitionMatrices[pos][posKey] = 1;

                        // Матрица в памяти
                        TransitionMatrixByPosition[prev - 1, curr - 1, pos]++;
                    }
                }
            }
        }

        private async Task CalculateForUnorderedGames(List<object> draws, List<System.Reflection.PropertyInfo> numberProps, int ballCount, int maxBallNumber, bool sortBalls)
        {
            var currentBalls = new List<int>();
            var previousBalls = new List<int>();

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                currentBalls.Clear();
                previousBalls.Clear();

                if (sortBalls)
                {
                    // Сортируем числа в текущем и предыдущем тираже
                    var currentTemp = new List<int>();
                    var previousTemp = new List<int>();
                    for (int j = 1; j <= ballCount; j++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(currentRow);
                            var valPrev = prop.GetValue(previousRow);
                            if (val != null && int.TryParse(val.ToString(), out int currNum) && currNum > 0)
                            {
                                currentTemp.Add(currNum);
                            }
                            if (valPrev != null && int.TryParse(valPrev.ToString(), out int prevNum) && prevNum > 0)
                            {
                                previousTemp.Add(prevNum);
                            }
                        }
                    }
                    currentTemp.Sort();
                    previousTemp.Sort();
                    currentBalls.AddRange(currentTemp);
                    previousBalls.AddRange(previousTemp);
                    // Добавляем нули в конец
                    while (currentBalls.Count < ballCount) currentBalls.Add(0);
                    while (previousBalls.Count < ballCount) previousBalls.Add(0);
                }
                else
                {
                    for (int j = 1; j <= ballCount; j++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(currentRow);
                            var valPrev = prop.GetValue(previousRow);
                            if (val != null && int.TryParse(val.ToString(), out int currNum))
                            {
                                currentBalls.Add(currNum);
                            }
                            else
                            {
                                currentBalls.Add(0);
                            }

                            if (valPrev != null && int.TryParse(valPrev.ToString(), out int prevNum))
                            {
                                previousBalls.Add(prevNum);
                            }
                            else
                            {
                                previousBalls.Add(0);
                            }
                        }
                        else
                        {
                            currentBalls.Add(0);
                            previousBalls.Add(0);
                        }
                    }

                    // Перемещаем все нули в конец списка
                    var currentFiltered = currentBalls.Where(x => x != 0).ToList();
                    var previousFiltered = previousBalls.Where(x => x != 0).ToList();
                    currentFiltered.AddRange(Enumerable.Repeat(0, ballCount - currentFiltered.Count));
                    previousFiltered.AddRange(Enumerable.Repeat(0, ballCount - previousFiltered.Count));
                    currentBalls = currentFiltered;
                    previousBalls = previousFiltered;
                }

                if (sortBalls)
                {
                    for (int pos = 0; pos < ballCount; pos++)
                    {
                        int curr = currentBalls[pos];
                        int prev = previousBalls[pos];

                        if (prev > 0 && prev <= maxBallNumber && curr > 0 && curr <= maxBallNumber)
                        {
                            // Общая матрица
                            var generalKey = (prev, curr);
                            if (GeneralTransitionMatrix.ContainsKey(generalKey))
                                GeneralTransitionMatrix[generalKey]++;
                            else
                                GeneralTransitionMatrix[generalKey] = 1;

                            // Матрица по позиции
                            var posKey = (prev, curr);
                            if (PositionTransitionMatrices[pos].ContainsKey(posKey))
                                PositionTransitionMatrices[pos][posKey]++;
                            else
                                PositionTransitionMatrices[pos][posKey] = 1;

                            // Матрица в памяти
                            TransitionMatrixByPosition[prev - 1, curr - 1, pos]++;
                        }
                    }
                }
                else
                {
                    for (int pos = 0; pos < ballCount; pos++)
                    {
                        int curr = currentBalls[pos];
                        for (int nextPos = 0; nextPos < ballCount; nextPos++)
                        {
                            int prev = previousBalls[nextPos];

                            if (prev > 0 && prev <= maxBallNumber && curr > 0 && curr <= maxBallNumber)
                            {
                                // Общая матрица
                                var generalKey = (prev, curr);
                                if (GeneralTransitionMatrix.ContainsKey(generalKey))
                                    GeneralTransitionMatrix[generalKey]++;
                                else
                                    GeneralTransitionMatrix[generalKey] = 1;

                                // Матрица по позиции (текущая позиция)
                                var posKey = (prev, curr);
                                if (PositionTransitionMatrices[pos].ContainsKey(posKey))
                                    PositionTransitionMatrices[pos][posKey]++;
                                else
                                    PositionTransitionMatrices[pos][posKey] = 1;

                                // Матрица в памяти
                                TransitionMatrixByPosition[prev - 1, curr - 1, pos]++;
                            }
                        }
                    }
                }
            }
        }
    }
}