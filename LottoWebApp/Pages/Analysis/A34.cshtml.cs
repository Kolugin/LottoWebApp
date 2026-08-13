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
    public class A34Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A34Model(AppServices s)
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
        public string? DifferenceType { get; set; } = "all";

        [BindProperty(SupportsGet = true)]
        public string? Title { get; set; }

        public DataTable? ResultsTable { get; set; }
        public int MaxBallNumber { get; set; }
        public int NumberOfBalls { get; set; }
        public bool HasBonus { get; set; }
        public bool HasTime { get; set; }

        public void Dispose()
        {
            ResultsTable?.Dispose();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A34_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            if (string.IsNullOrEmpty(DifferenceType))
                DifferenceType = "all";

            await GenerateDifferenceMap();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            if (string.IsNullOrEmpty(DifferenceType))
                DifferenceType = "all";

            await GenerateDifferenceMap();
            return Page();
        }

        private async Task GenerateDifferenceMap()
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
            (MaxBallNumber, NumberOfBalls, HasBonus, HasTime) = Game?.ToLower() switch
            {
                "keno" => (60, 20, false, false),
                "blitz" => (20, 8, true, true),
                "5x36" => (36, 6, false, false),
                "6x49" => (49, 6, false, false),
                "1224" => (24, 12, false, true),
                _ => (60, 20, false, false)
            };

            ResultsTable = new DataTable();
            ResultsTable.Columns.Add("Тираж", typeof(int));
            ResultsTable.Columns.Add("Дата", typeof(string));
            if (HasTime)
                ResultsTable.Columns.Add("Время", typeof(string));

            var positionProps = numberProps.Where(p => p.Name.StartsWith("B") && p.Name != "BB").ToList();
            int posCount = positionProps.Count;

            if (DifferenceType == "all")
            {
                // Все позиции
                for (int i = 0; i < posCount; i++)
                {
                    for (int j = i + 1; j < posCount; j++)
                    {
                        string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[j].Name.Substring(1)}";
                        ResultsTable.Columns.Add(colName, typeof(string));
                    }
                }
            }
            else
            {
                // Соседние позиции
                for (int i = 0; i < posCount - 1; i++)
                {
                    string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[i + 1].Name.Substring(1)}";
                    ResultsTable.Columns.Add(colName, typeof(string));
                }
            }

            foreach (var row in draws)
            {
                var newRow = ResultsTable.NewRow();
                newRow["Тираж"] = Convert.ToInt32(first.GetType().GetProperty("Draw")?.GetValue(row) ?? 0);
                newRow["Дата"] = first.GetType().GetProperty("Date")?.GetValue(row)?.ToString() ?? "";
                if (HasTime)
                    newRow["Время"] = first.GetType().GetProperty("Time")?.GetValue(row)?.ToString() ?? "";

                if (DifferenceType == "all")
                {
                    for (int i = 0; i < posCount; i++)
                    {
                        var val1 = positionProps[i].GetValue(row);
                        if (val1 == null || !int.TryParse(val1.ToString(), out int b1) || b1 == 0) continue;

                        for (int j = i + 1; j < posCount; j++)
                        {
                            var val2 = positionProps[j].GetValue(row);
                            int b2 = val2 != null && int.TryParse(val2.ToString(), out int num) && num != 0 ? num : 0;
                            string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[j].Name.Substring(1)}";
                            newRow[colName] = b2 == 0 ? "-" : Math.Abs(b1 - b2).ToString();
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < posCount - 1; i++)
                    {
                        var val1 = positionProps[i].GetValue(row);
                        var val2 = positionProps[i + 1].GetValue(row);

                        if (val1 != null && val2 != null && int.TryParse(val1.ToString(), out int ball1) && int.TryParse(val2.ToString(), out int ball2))
                        {
                            string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[i + 1].Name.Substring(1)}";
                            if (ball1 == 0 || ball2 == 0)
                                newRow[colName] = "-";
                            else
                                newRow[colName] = Math.Abs(ball1 - ball2).ToString();
                        }
                        else
                        {
                            string colName = $"{positionProps[i].Name.Substring(1)}_{positionProps[i + 1].Name.Substring(1)}";
                            newRow[colName] = "-";
                        }
                    }
                }

                ResultsTable.Rows.Add(newRow);
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            if (string.IsNullOrEmpty(DifferenceType))
                return BadRequest("Тип разницы обязателен.");

            return await _s.Csv34.ExportAsync(Game, DrawCount, Direction, DifferenceType, Title);
        }
    }
}