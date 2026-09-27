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
        public bool HasTime { get; set; }
        public bool Is320 { get; set; }

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

            Is320 = Game.Equals("320", StringComparison.OrdinalIgnoreCase);

            // Определяем количество шаров для игры
            (NumberOfBalls, HasTime) = Game?.ToLower() switch
            {
                "keno" => (20, false),
                "blitz" => (8, true),
                "5x36" => (6, false),
                "6x49" => (6, false),
                "1224" => (12, true),
                "320" => (6, true),
                _ => (20, false)
            };

            // Имена свойств для 320
            var propNames320 = new[] { "B11", "B12", "B13", "B21", "B22", "B23" };

            ResultsTable = new DataTable();
            ResultsTable.Columns.Add("Тираж", typeof(int));
            ResultsTable.Columns.Add("Дата", typeof(string));
            if (HasTime)
                ResultsTable.Columns.Add("Время", typeof(string));

            for (int i = 1; i <= NumberOfBalls; i++)
            {
                ResultsTable.Columns.Add($"№П {i}", typeof(string));
            }

            int rowCount = draws.Count;
            int baseColumns = HasTime ? 3 : 2;

            for (int i = 0; i < rowCount; i++)
            {
                var currentRow = draws[i];
                var values = new object[baseColumns + NumberOfBalls];

                var drawProp = first.GetType().GetProperty("Draw");
                var dateProp = first.GetType().GetProperty("Date");
                var timeProp = HasTime ? first.GetType().GetProperty("Time") : null;

                values[0] = Convert.ToInt32(drawProp?.GetValue(currentRow) ?? 0);
                values[1] = dateProp?.GetValue(currentRow)?.ToString() ?? "";

                int colIndex = 2;
                if (HasTime && timeProp != null)
                {
                    values[colIndex] = timeProp.GetValue(currentRow)?.ToString() ?? "";
                    colIndex++;
                }

                for (int j = 1; j <= NumberOfBalls; j++)
                {
                    int value = 0;

                    if (Is320)
                    {
                        // Для 320 читаем по именам B11, B12, B13, B21, B22, B23
                        var prop = first.GetType().GetProperty(propNames320[j - 1]);
                        if (prop != null)
                        {
                            var val = prop.GetValue(currentRow);
                            if (val != null && int.TryParse(val.ToString(), out int num))
                            {
                                value = num;
                            }
                        }
                    }
                    else
                    {
                        var prop = numberProps.FirstOrDefault(p => p.Name == $"B{j}");
                        if (prop != null)
                        {
                            var val = prop.GetValue(currentRow);
                            if (val != null && int.TryParse(val.ToString(), out int num))
                            {
                                value = num;
                            }
                        }
                    }

                    if (value == 0)
                    {
                        values[colIndex] = "-";
                    }
                    else
                    {
                        values[colIndex] = (value % 2 == 0) ? "Ч" : "Н";
                    }
                    colIndex++;
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