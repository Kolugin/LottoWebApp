using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace LottoWebApp.Pages.Analysis
{
    [Authorize]
    public class A32Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A32Model(AppServices s)
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
        public bool EnableSorting { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? SelectedTable { get; set; }

        public Dictionary<(int Number, int Position), int>? DistributionData { get; set; }
        public Dictionary<string, Dictionary<(int Number, int Position), int>> DistributionDataByField { get; set; } = new();
        public int MaxBallNumber { get; set; }
        public int NumberOfBalls { get; set; }
        public bool IsAlwaysUnordered { get; set; }
        public bool Is320 { get; set; }

        public void Dispose()
        {
            DistributionData?.Clear();
            DistributionDataByField?.Clear();
        }

        public async Task OnGetAsync()
        {
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            IsAlwaysUnordered = (Game?.ToLower() == "1224" || Game?.ToLower() == "blitz");
            if (IsAlwaysUnordered) EnableSorting = false;

            Is320 = Game.Equals("320", StringComparison.OrdinalIgnoreCase);

            await GenerateTotalOccurrencesTable();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            IsAlwaysUnordered = (Game?.ToLower() == "1224" || Game?.ToLower() == "blitz");
            if (IsAlwaysUnordered) EnableSorting = false;

            Is320 = Game.Equals("320", StringComparison.OrdinalIgnoreCase);

            await GenerateTotalOccurrencesTable();
            return Page();
        }

        private async Task GenerateTotalOccurrencesTable()
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

            if (Is320)
            {
                MaxBallNumber = 20;
                NumberOfBalls = 3;

                var fields = new[] { "B1", "B2" };
                foreach (var field in fields)
                {
                    var propNames = new[] { $"{field}1", $"{field}2", $"{field}3" };
                    var fieldOccurrences = new Dictionary<(int Number, int Position), int>();

                    foreach (var row in draws)
                    {
                        var currentBalls = new List<int>();

                        foreach (var propName in propNames)
                        {
                            var prop = first.GetType().GetProperty(propName);
                            if (prop != null)
                            {
                                var val = prop.GetValue(row);
                                if (val != null && int.TryParse(val.ToString(), out int ball) && ball > 0)
                                {
                                    currentBalls.Add(ball);
                                }
                            }
                        }

                        if (EnableSorting && !IsAlwaysUnordered)
                        {
                            currentBalls.Sort();
                        }

                        for (int pos = 0; pos < currentBalls.Count; pos++)
                        {
                            int currentBall = currentBalls[pos];
                            if (currentBall > 0)
                            {
                                var key = (currentBall, pos);
                                if (fieldOccurrences.ContainsKey(key))
                                    fieldOccurrences[key]++;
                                else
                                    fieldOccurrences[key] = 1;
                            }
                        }
                    }

                    DistributionDataByField[field] = fieldOccurrences;
                }
            }
            else
            {
                var numberProps = first.GetType().GetProperties()
                    .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                    .OrderBy(p => p.Name == "BB" ? 999 : int.Parse(p.Name.Substring(1)))
                    .ToList();

                if (!numberProps.Any()) return;

                (MaxBallNumber, NumberOfBalls) = Game?.ToLower() switch
                {
                    "keno" => (60, 20),
                    "blitz" => (20, 8),
                    "5x36" => (36, 6),
                    "6x49" => (49, 6),
                    "1224" => (24, 12),
                    _ => (60, 20)
                };

                var totalOccurrencesByPosition = new Dictionary<(int Number, int Position), int>();

                foreach (var row in draws)
                {
                    var currentBalls = new List<int>();

                    for (int i = 1; i <= NumberOfBalls; i++)
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{i}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(row);
                            if (val != null && int.TryParse(val.ToString(), out int ball) && ball > 0)
                            {
                                currentBalls.Add(ball);
                            }
                        }
                    }

                    if (EnableSorting && !IsAlwaysUnordered)
                    {
                        currentBalls.Sort();
                    }

                    for (int pos = 0; pos < currentBalls.Count; pos++)
                    {
                        int currentBall = currentBalls[pos];
                        if (currentBall > 0)
                        {
                            var key = (currentBall, pos);
                            if (totalOccurrencesByPosition.ContainsKey(key))
                                totalOccurrencesByPosition[key]++;
                            else
                                totalOccurrencesByPosition[key] = 1;
                        }
                    }
                }

                DistributionData = totalOccurrencesByPosition;
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv32.ExportAsync(Game, DrawCount, Direction, EnableSorting, Title, SelectedTable);
        }
    }
}