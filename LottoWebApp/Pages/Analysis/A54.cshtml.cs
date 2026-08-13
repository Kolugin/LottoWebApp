using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Globalization;
using System.Linq;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A54Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A54Model(AppServices s)
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

        public List<List<int>>? PredictedNumbersByPosition { get; set; }
        public List<List<double>>? PredictedChancesByPosition { get; set; } // Добавлено
        public int NumberOfBalls { get; set; }
        public int MaxBallNumber { get; set; }
        public int MaxRows { get; set; }

        public void Dispose()
        {
            PredictedNumbersByPosition?.Clear();
            PredictedChancesByPosition?.Clear(); // Добавлено
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A54_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await PredictNextDrawTable();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await PredictNextDrawTable();
            return Page();
        }

        private async Task PredictNextDrawTable()
        {
            var query = new LotteryQueryProvider(_s.Db).GetQuery(Game);

            bool newestFirst = Direction == "newToOld";

            if (!newestFirst)
            {
                query = query.OrderBy(e => EF.Property<int>(e, "Draw"));

                if (DrawCount > 0)
                    query = query.Take((int)DrawCount);
            }
            else
            {
                query = query.OrderByDescending(e => EF.Property<int>(e, "Draw"));

                if (DrawCount > 0)
                    query = query.Take((int)DrawCount);

                query = query.OrderBy(e => EF.Property<int>(e, "Draw"));
            }

            var result = await query.ToListAsync();
            //===========================================================================================================
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
            (NumberOfBalls, MaxBallNumber) = Game?.ToLower() switch
            {
                "keno" => (20, 60),
                "blitz" => (8, 20),
                "5x36" => (6, 36),
                "6x49" => (6, 49),
                "1224" => (12, 24),
                _ => (20, 60)
            };

            var matrix = new int[MaxBallNumber, MaxBallNumber, NumberOfBalls];

            bool isOrdered = (Game?.ToLower() == "blitz" || Game?.ToLower() == "1224");

            // Автоматическое определение необходимости сортировки
            bool autoSort = !(Game?.ToLower() == "blitz" || Game?.ToLower() == "1224");

            if (isOrdered)
                await CalculateForOrderedGames(draws, numberProps, matrix);
            else
                await CalculateForUnorderedGames(draws, numberProps, matrix, autoSort);

            var lastDraw = draws.Last();
            var lastBalls = new int[NumberOfBalls];

            for (int i = 0; i < NumberOfBalls; i++)
            {
                var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i + 1}");
                if (prop != null)
                {
                    var val = prop.GetValue(lastDraw);
                    if (val != null && int.TryParse(val.ToString(), out int num))
                    {
                        lastBalls[i] = num;
                    }
                }
            }

            if (autoSort)
            {
                lastBalls = lastBalls.Where(x => x > 0).OrderBy(x => x).ToArray();
                if (lastBalls.Length < NumberOfBalls)
                {
                    var zeros = Enumerable.Repeat(0, NumberOfBalls - lastBalls.Length);
                    lastBalls = lastBalls.Concat(zeros).ToArray();
                }
            }

            // Получение списка вероятных чисел по позициям с сортировкой по убыванию веса
            PredictedNumbersByPosition = new List<List<int>>();
            PredictedChancesByPosition = new List<List<double>>(); // Инициализация

            for (int pos = 0; pos < NumberOfBalls; pos++)
            {
                var possible = new List<(int Number, int Weight)>();
                int prev = lastBalls[pos];

                if (prev > 0 && prev <= MaxBallNumber)
                {
                    for (int next = 0; next < MaxBallNumber; next++)
                    {
                        int weight = matrix[prev - 1, next, pos];
                        if (weight > 0)
                            possible.Add((next + 1, weight));
                    }
                }

                var sortedPossible = possible.OrderByDescending(p => p.Weight).ToList();

                // Разделяем числа и шансы
                var numbers = sortedPossible.Select(p => p.Number).ToList();
                var chances = new List<double>();

                if (sortedPossible.Any())
                {
                    int totalWeight = sortedPossible.Sum(p => p.Weight);
                    chances = sortedPossible.Select(p => (double)p.Weight / totalWeight).ToList();
                }

                PredictedNumbersByPosition.Add(numbers);
                PredictedChancesByPosition.Add(chances); // Добавляем шансы
            }

            MaxRows = PredictedNumbersByPosition.Any() ? PredictedNumbersByPosition.Max(list => list.Count) : 0;
        }

        private async Task CalculateForOrderedGames(List<object> draws, List<System.Reflection.PropertyInfo> numberProps, int[,,] matrix)
        {
            var currentBalls = new List<int>();
            var previousBalls = new List<int>();

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                currentBalls.Clear();
                previousBalls.Clear();

                for (int j = 1; j <= NumberOfBalls; j++)
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

                for (int pos = 0; pos < NumberOfBalls; pos++)
                {
                    int curr = currentBalls[pos];
                    int prev = previousBalls[pos];

                    if (prev > 0 && prev <= MaxBallNumber && curr > 0 && curr <= MaxBallNumber)
                    {
                        matrix[prev - 1, curr - 1, pos]++;
                    }
                }
            }
        }

        private async Task CalculateForUnorderedGames(List<object> draws, List<System.Reflection.PropertyInfo> numberProps, int[,,] matrix, bool sortBalls)
        {
            var currentBalls = new List<int>();
            var previousBalls = new List<int>();

            for (int i = 1; i < draws.Count; i++)
            {
                var currentRow = draws[i];
                var previousRow = draws[i - 1];

                currentBalls.Clear();
                previousBalls.Clear();

                for (int j = 1; j <= NumberOfBalls; j++)
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

                if (sortBalls)
                {
                    currentBalls = currentBalls.Where(x => x != 0).OrderBy(x => x).ToList();
                    previousBalls = previousBalls.Where(x => x != 0).OrderBy(x => x).ToList();

                    currentBalls.AddRange(Enumerable.Repeat(0, NumberOfBalls - currentBalls.Count));
                    previousBalls.AddRange(Enumerable.Repeat(0, NumberOfBalls - previousBalls.Count));

                    for (int pos = 0; pos < NumberOfBalls; pos++)
                    {
                        int curr = currentBalls[pos];
                        int prev = previousBalls[pos];

                        if (prev > 0 && prev <= MaxBallNumber && curr > 0 && curr <= MaxBallNumber)
                        {
                            matrix[prev - 1, curr - 1, pos]++;
                        }
                    }
                }
                else
                {
                    for (int pos = 0; pos < NumberOfBalls; pos++)
                    {
                        int curr = currentBalls[pos];
                        for (int nextPos = 0; nextPos < NumberOfBalls; nextPos++)
                        {
                            int prev = previousBalls[nextPos];

                            if (prev > 0 && prev <= MaxBallNumber && curr > 0 && curr <= MaxBallNumber)
                            {
                                matrix[prev - 1, curr - 1, pos]++;
                            }
                        }
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            // Автоматическое определение необходимости сортировки
            bool autoSort = !(Game?.ToLower() == "blitz" || Game?.ToLower() == "1224");
            return await _s.Csv54.ExportAsync(Game, DrawCount, Direction, autoSort, Title);
        }
    }
}