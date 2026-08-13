using DocumentFormat.OpenXml.Wordprocessing;
using LottoWebApp.Data;
using LottoWebApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Reflection;

namespace LottoWebApp.Services
{
    public class A12CsvExportHelper
    {
        private readonly LottoDbContext _db;
        public A12CsvExportHelper(LottoDbContext db)
        {
            _db = db;
        }

        public async Task<FileResult> ExportAsync(string? game, int? drawCount, string? direction, string? title)
        {
            if (string.IsNullOrEmpty(game))
                game = "keno";

            var query = new LotteryQueryProvider(_db).GetQuery(game);

            bool isDesc = direction == "newToOld";
            query = isDesc
                ? query.OrderByDescending(e => EF.Property<int>(e, "Draw"))
                : query.OrderBy(e => EF.Property<int>(e, "Draw"));

            if (drawCount.HasValue && drawCount.Value > 0)
                query = query.Take(drawCount.Value);

            var result = await query.ToListAsync();
            if (!result.Any())
                throw new InvalidOperationException("Нет данных для экспорта.");

            // Создание DataTable - ЛОГИКА ТАКАЯ ЖЕ, КАК В A12Model.OnGetAsync (сортировка чисел)
            DataTable dt = new DataTable();
            dt.Columns.Add("Тираж");
            dt.Columns.Add("Дата");

            bool showTime = !(game.Equals("keno", StringComparison.OrdinalIgnoreCase)
                             || game.Equals("5x36", StringComparison.OrdinalIgnoreCase)
                             || game.Equals("6x49", StringComparison.OrdinalIgnoreCase));
            if (showTime)
                dt.Columns.Add("Время");

            var first = result.First();
            var numberProps = first.GetType().GetProperties()
                .Where(p => (p.Name.StartsWith("B") && int.TryParse(p.Name.Substring(1), out _)) || p.Name == "BB")
                .ToList();

            // Сортировка: обычные B1..Bn по номеру, BB в конец (для определения порядка извлекаемых значений)
            numberProps.Sort((p1, p2) =>
            {
                if (p1.Name == "BB") return 1;
                if (p2.Name == "BB") return -1;
                return int.Parse(p1.Name.Substring(1)).CompareTo(int.Parse(p2.Name.Substring(1)));
            });

            // Заголовки: те же, что и в A11, чтобы столбцы были в правильном порядке при извлечении
            foreach (var p in numberProps)
            {
                dt.Columns.Add(p.Name == "BB" ? "Бонус" : "№ " + p.Name.Substring(1));
            }

            foreach (var item in result)
            {
                var row = dt.NewRow();
                int col = 0;
                row[col++] = Convert.ToInt32(item.GetType().GetProperty("Draw")?.GetValue(item));
                row[col++] = Convert.ToDateTime(item.GetType().GetProperty("Date")?.GetValue(item)).ToString("dd.MM.yyyy");

                if (showTime)
                    row[col++] = item.GetType().GetProperty("Time")?.GetValue(item)?.ToString() ?? "";

                List<int> nums = new();
                int? bonus = null;

                foreach (var p in numberProps)
                {
                    var val = p.GetValue(item);
                    if (val == null) continue;

                    if (p.Name == "BB")
                    {
                        if (int.TryParse(val.ToString(), out int b))
                            bonus = b;
                    }
                    else
                    {
                        if (int.TryParse(val.ToString(), out int n))
                            nums.Add(n);
                    }
                }

                nums = nums.Where(n => n > 0).OrderBy(n => n).ToList();

                int positionCount = numberProps.Count(p => p.Name != "BB");
                while (nums.Count < positionCount)
                    nums.Add(-1);

                if (bonus.HasValue)
                    nums.Add(bonus.Value);

                foreach (var n in nums)
                    row[col++] = n > 0 ? n.ToString() : "-";
                dt.Rows.Add(row);
            }


            // Имя файла
            string titlePart = string.IsNullOrEmpty(title) ? "A12_Export" : title;
            string drawCountStr = drawCount?.ToString() ?? "all";

            string safeFileName = $"{titlePart}_{game}_{drawCountStr}_{DateTime.Now:yyyyMMddHHmmss}.csv"
                        .Replace(" ", "_")
                        .Replace(":", "_")
                        .Replace("/", "_")
                        .Replace("\\", "_");

            string filePath = await CsvExportService.ExportToCsvAsync(dt, safeFileName);

            string webRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            string relativePathWithoutSlash = filePath.TrimStart('/');
            string fullPath = Path.Combine(webRootPath, relativePathWithoutSlash);

            if (!System.IO.File.Exists(fullPath))
                throw new FileNotFoundException("Файл для экспорта не был создан.", fullPath);

            var bytes = await System.IO.File.ReadAllBytesAsync(fullPath);
            return new FileContentResult(bytes, "text/csv") { FileDownloadName = Path.GetFileName(fullPath) };
        }
    }
}