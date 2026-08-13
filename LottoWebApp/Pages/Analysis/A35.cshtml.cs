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
    public class A35Model : PageModel, IDisposable
    {
        private readonly AppServices _s;
        public A35Model(AppServices s)
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

        public DataTable? ResultsTable { get; set; }
        public int NumberOfBalls { get; set; }

        public void Dispose()
        {
            ResultsTable?.Dispose();
        }

        public async Task OnGetAsync()
        {
            string cacheKey = $"A35_{Game}_{DrawCount}_{Direction}";
            if (string.IsNullOrEmpty(Game))
                Game = "keno";

            await GenerateEvenOddTable();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return Page();

            await GenerateEvenOddTable();
            return Page();
        }

        private async Task GenerateEvenOddTable()
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

            // Определяем количество шаров для игры
            NumberOfBalls = Game?.ToLower() switch
            {
                "keno" => 20,
                "blitz" => 8,
                "5x36" => 6,
                "6x49" => 6,
                "1224" => 12,
                _ => 20
            };

            ResultsTable = new DataTable();
            ResultsTable.Columns.Add("Тираж", typeof(int));
            ResultsTable.Columns.Add("Дата", typeof(string));
            for (int i = 1; i <= NumberOfBalls; i++)
            {
                ResultsTable.Columns.Add($"№П {i}", typeof(string));
            }

            int rowCount = draws.Count;

            for (int i = 0; i < rowCount; i++)
            {
                var currentRow = draws[i];
                var values = new object[2 + NumberOfBalls];

                var drawProp = first.GetType().GetProperty("Draw");
                var dateProp = first.GetType().GetProperty("Date");

                values[0] = Convert.ToInt32(drawProp?.GetValue(currentRow) ?? 0);
                values[1] = dateProp?.GetValue(currentRow)?.ToString() ?? "";

                for (int j = 1; j <= NumberOfBalls; j++)
                {
                    var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                    int value = 0;
                    if (prop != null)
                    {
                        var val = prop.GetValue(currentRow);
                        if (val != null && int.TryParse(val.ToString(), out int num))
                        {
                            value = num;
                        }
                    }

                    if (value == 0)
                    {
                        values[j + 1] = "-";
                    }
                    else
                    {
                        values[j + 1] = (value % 2 == 0) ? "Ч" : "Н";
                    }
                }

                ResultsTable.Rows.Add(values);
            }
        }

        public async Task<IActionResult> OnPostDownloadCsvAsync()
        {
            if (string.IsNullOrEmpty(Game))
                return BadRequest("Игра обязательна.");

            return await _s.Csv35.ExportAsync(Game, DrawCount, Direction, Title);
        }
    }
}