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
    public class A33Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A33Model(AppServices s)
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
        public int CombinationSize { get; set; } = 2;

        [BindProperty(SupportsGet = true)]
        public bool IncludeBonus { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        public List<GeneratedFileResult> GeneratedFiles { get; set; } = new();
        public int MaxBallNumber { get; set; }
        public int NumberOfBalls { get; set; }
        public bool CanIncludeBonus { get; set; }
        public int MaxCombinationSize { get; set; }

        public class GeneratedFileResult
        {
            public string FileName { get; set; } = "";
            public string RelativePath { get; set; } = "";
        }

        public void Dispose()
        {
            GeneratedFiles?.Clear();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A33_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            SetGameParameters();
            await GenerateComboAsync();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            SetGameParameters();
            await GenerateComboAsync();
            return Page();
        }

        private void SetGameParameters()
        {
            (MaxBallNumber, NumberOfBalls, CanIncludeBonus) = Game?.ToLower() switch
            {
                "keno" => (60, 20, false),
                "blitz" => (20, 8, true),
                "5x36" => (36, 6, false),
                "6x49" => (49, 6, false),
                "1224" => (24, 12, false),
                _ => (60, 20, false)
            };

            MaxCombinationSize = Game?.ToLower() switch
            {
                "keno" => 10,
                "blitz" => IncludeBonus ? 8 : 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => Math.Min(MaxBallNumber, 12)
            };

            if (CombinationSize > MaxCombinationSize)
                CombinationSize = MaxCombinationSize;

            if (!CanIncludeBonus)
                IncludeBonus = false;
        }

        private async Task GenerateComboAsync()
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

            if (IncludeBonus && !CanIncludeBonus)
                return;

            // Генерация комбинаций
            var baseCombinations = GenerateCombinations(CombinationSize, 1, MaxBallNumber).ToList();
            var parsedBaseCombinations = baseCombinations
                .Select(c => c.Split('_').Select(int.Parse).ToArray())
                .ToArray();

            List<string> combinations;
            List<(int[] Main, int Bonus)> allParsedBonusCombos = new(); // для бонусных

            if (IncludeBonus)
            {
                combinations = new List<string>();
                allParsedBonusCombos = new List<(int[] Main, int Bonus)>();

                foreach (var mainCombo in baseCombinations)
                {
                    var mainNumbers = mainCombo.Split('_').Select(int.Parse).ToArray();
                    for (int bonus = 1; bonus <= 4; bonus++)
                    {
                        string comboWithBonus = $"{mainCombo}_{bonus}Б";
                        combinations.Add(comboWithBonus);
                        allParsedBonusCombos.Add((mainNumbers, bonus));
                    }
                }
            }
            else
            {
                combinations = baseCombinations;
                allParsedBonusCombos = null;
            }

            var matchedMap = new Dictionary<int, HashSet<int>>();

            var drawBallsMap = new Dictionary<int, HashSet<int>>();
            var bonusBallMap = new Dictionary<int, int>();

            foreach (var row in draws)
            {
                int drawNumber = Convert.ToInt32(first.GetType().GetProperty("Draw")?.GetValue(row) ?? 0);

                var drawBalls = new HashSet<int>();
                for (int i = 1; i <= NumberOfBalls; i++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                    if (prop != null)
                    {
                        var val = prop.GetValue(row);
                        if (val != null && int.TryParse(val.ToString(), out int ball) && ball > 0)
                        {
                            drawBalls.Add(ball);
                        }
                    }
                }

                drawBallsMap[drawNumber] = drawBalls;

                if (IncludeBonus)
                {
                    var bbProp = numberProps.FirstOrDefault(p => p.Name == "BB");
                    if (bbProp != null)
                    {
                        var bbVal = bbProp.GetValue(row);
                        if (bbVal != null && int.TryParse(bbVal.ToString(), out int bonusBall))
                        {
                            bonusBallMap[drawNumber] = bonusBall;
                        }
                    }
                }
            }

            foreach (var drawEntry in drawBallsMap)
            {
                int drawNumber = drawEntry.Key;
                var drawBalls = drawEntry.Value;

                var matchedIndexes = new HashSet<int>();

                if (IncludeBonus && bonusBallMap.TryGetValue(drawNumber, out int bonusBall))
                {
                    // Проверяем все комбинации основных чисел с каждым возможным бонусом
                    for (int i = 0; i < allParsedBonusCombos.Count; i++)
                    {
                        var (mainNumbers, expectedBonus) = allParsedBonusCombos[i];
                        if (mainNumbers.All(drawBalls.Contains) && bonusBall == expectedBonus)
                            matchedIndexes.Add(i);
                    }
                }
                else
                {
                    // Только основные числа
                    for (int i = 0; i < parsedBaseCombinations.Length; i++)
                    {
                        if (parsedBaseCombinations[i].All(drawBalls.Contains))
                            matchedIndexes.Add(i);
                    }
                }

                matchedMap[drawNumber] = matchedIndexes;
            }

            // Экспорт результатов в CSV
            string baseFileName = $"3.3_{Game.ToUpper()}_Комбинация_{CombinationSize}_чисел{(IncludeBonus ? "_+1Б" : "")}";
            string extension = ".csv";
            string fullBasePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", baseFileName);

            int maxRowsPerFile = 500000;
            int maxColumnsPerFile = 15000;

            var baseHeaders = new List<string> { "Тираж", "Дата" };
            if (Game?.ToLower() == "blitz" || Game?.ToLower() == "1224")
                baseHeaders.Add("Время");

            int totalCombinations = combinations.Count;
            var sortedDraws = draws.OrderBy(d => Convert.ToInt32(d.GetType().GetProperty("Draw")?.GetValue(d) ?? 0)).ToList();
            int totalRows = sortedDraws.Count;

            int columnBlockIndex = 1;

            for (int comboStart = 0; comboStart < totalCombinations; comboStart += maxColumnsPerFile)
            {
                int comboEnd = Math.Min(comboStart + maxColumnsPerFile, totalCombinations);
                var currentComboBlock = combinations.GetRange(comboStart, comboEnd - comboStart);

                int rowBlockIndex = 1;
                int rowCounter = 0;
                string currentFilePath = $"{fullBasePath}_{columnBlockIndex}_{rowBlockIndex}{extension}";
                var writer = new System.IO.StreamWriter(currentFilePath, false, System.Text.Encoding.UTF8);

                try
                {
                    var header = new List<string>(baseHeaders);
                    header.AddRange(currentComboBlock);
                    await writer.WriteLineAsync(string.Join(";", header));

                    foreach (var row in sortedDraws)
                    {
                        if (rowCounter >= maxRowsPerFile)
                        {
                            await writer.FlushAsync();
                            await writer.DisposeAsync();

                            // Добавляем файл в список перед созданием нового
                            string relativePath = currentFilePath.Replace(Directory.GetCurrentDirectory() + "\\wwwroot\\", "").Replace('\\', '/');
                            GeneratedFiles.Add(new GeneratedFileResult
                            {
                                FileName = Path.GetFileName(currentFilePath),
                                RelativePath = relativePath
                            });

                            rowBlockIndex++;
                            rowCounter = 0;

                            currentFilePath = $"{fullBasePath}_{columnBlockIndex}_{rowBlockIndex}{extension}";
                            writer = new System.IO.StreamWriter(currentFilePath, false, System.Text.Encoding.UTF8);

                            await writer.WriteLineAsync(string.Join(";", header));
                        }

                        int draw = Convert.ToInt32(first.GetType().GetProperty("Draw")?.GetValue(row) ?? 0);
                        var dateProp = first.GetType().GetProperty("Date");
                        string date = dateProp?.GetValue(row)?.ToString() ?? "";

                        var timeProp = (Game?.ToLower() == "blitz" || Game?.ToLower() == "1224") ? first.GetType().GetProperty("Time") : null;
                        string time = timeProp?.GetValue(row)?.ToString() ?? "";

                        var line = new List<string> { draw.ToString(), date };
                        if (!string.IsNullOrEmpty(time))
                            line.Add(time);

                        var matched = matchedMap.ContainsKey(draw) ? matchedMap[draw] : new HashSet<int>();
                        for (int i = comboStart; i < comboEnd; i++)
                        {
                            line.Add(matched.Contains(i) ? "1" : "0");
                        }

                        await writer.WriteLineAsync(string.Join(";", line));
                        rowCounter++;
                    }
                }
                finally
                {
                    await writer.DisposeAsync();
                }

                // Добавляем последний файл в блоке колонок
                string relativePathLast = currentFilePath.Replace(Directory.GetCurrentDirectory() + "\\wwwroot\\", "").Replace('\\', '/');
                GeneratedFiles.Add(new GeneratedFileResult
                {
                    FileName = Path.GetFileName(currentFilePath),
                    RelativePath = relativePathLast
                });

                columnBlockIndex++;
            }
        }

        private static IEnumerable<string> GenerateCombinations(int combinationSize, int start, int end)
        {
            var buffer = new int[combinationSize];
            return GenerateCombinationsRecursive(buffer, 0, combinationSize, start, end);
        }

        private static IEnumerable<string> GenerateCombinationsRecursive(int[] buffer, int index, int combinationSize, int start, int end)
        {
            if (index == combinationSize)
            {
                yield return string.Join("_", buffer);
                yield break;
            }

            for (int i = start; i <= end; i++)
            {
                buffer[index] = i;
                foreach (var combo in GenerateCombinationsRecursive(buffer, index + 1, combinationSize, i + 1, end))
                    yield return combo;
            }
        }
    }
}