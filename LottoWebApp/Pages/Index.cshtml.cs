using Microsoft.AspNetCore.Mvc.RazorPages;
using LottoWebApp.Data;
using LottoWebApp.Models;
using Microsoft.EntityFrameworkCore;

namespace LottoWebApp.Pages
{
    public class IndexModel : PageModel
    {
        private readonly LottoDbContext _context;

        public IndexModel(LottoDbContext context)
        {
            _context = context;
        }

        // Словарь: название лотереи список последних 5 тиражей
        public Dictionary<string, List<LotteryDrawItem>> LotteryData { get; set; } = new();

        public async Task OnGetAsync()
        {
            // Получаем последние 5 тиражей для каждой лотереи
            var last1224 = await _context.Lottery1224BY
                .OrderByDescending(d => d.Draw)
                .Take(5)
                .ToListAsync();

            var last536 = await _context.Lottery536BY
                .OrderByDescending(d => d.Draw)
                .Take(5)
                .ToListAsync();

            var last649 = await _context.Lottery649BY
                .OrderByDescending(d => d.Draw)
                .Take(5)
                .ToListAsync();

            var lastBlitz = await _context.LotteryBlitzBY
                .OrderByDescending(d => d.Draw)
                .Take(5)
                .ToListAsync();

            var lastKeno = await _context.LotteryKenoBY
                .OrderByDescending(d => d.Draw)
                .Take(5)
                .ToListAsync();

            var last320 = await _context.Lottery320BY
               .OrderByDescending(d => d.Draw)
               .Take(5)
               .ToListAsync();

            // Преобразуем в общий формат для отображения
            LotteryData["12/24"] = last1224.Select(d => new LotteryDrawItem
            {
                Draw = d.Draw,
                Date = d.Date,
                Time = d.Time,
                Numbers = string.Join(", ", new[] { d.B1, d.B2, d.B3, d.B4, d.B5, d.B6, d.B7, d.B8, d.B9, d.B10, d.B11, d.B12 }.Where(n => n != 0))
            }).ToList();

            LotteryData["5 из 36"] = last536.Select(d => new LotteryDrawItem
            {
                Draw = d.Draw,
                Date = d.Date,
                Numbers = string.Join(", ", new[] { d.B1, d.B2, d.B3, d.B4, d.B5, d.B6 }.Where(n => n != 0))
            }).ToList();

            LotteryData["6 из 49"] = last649.Select(d => new LotteryDrawItem
            {
                Draw = d.Draw,
                Date = d.Date,
                Numbers = string.Join(", ", new[] { d.B1, d.B2, d.B3, d.B4, d.B5, d.B6 }.Where(n => n != 0))
            }).ToList();

            LotteryData["Блиц"] = lastBlitz.Select(d => new LotteryDrawItem
            {
                Draw = d.Draw,
                Date = d.Date,
                Time = d.Time, 
                Numbers = string.Join(", ", new[] { d.B1, d.B2, d.B3, d.B4, d.B5, d.B6, d.B7, d.B8 }.Where(n => n != 0))
            }).ToList();

            LotteryData["Кено"] = lastKeno.Select(d => new LotteryDrawItem
            {
                Draw = d.Draw,
                Date = d.Date,
                Numbers = string.Join(", ", new[] { d.B1, d.B2, d.B3, d.B4, d.B5, d.B6, d.B7, d.B8, d.B9, d.B10, d.B11, d.B12, d.B13, d.B14, d.B15, d.B16, d.B17, d.B18, d.B19, d.B20 }.Where(n => n != 0))
            }).ToList();

            LotteryData["3+3"] = last320.Select(d => new LotteryDrawItem
            {
                Draw = d.Draw,
                Date = d.Date,
                Time = d.Time,
                Numbers = string.Join(", ", new[] { d.B11, d.B12, d.B13, d.B21, d.B22, d.B23 }.Where(n => n != 0))
            }).ToList();
        }
    }

    public class LotteryDrawItem
    {
        public int Draw { get; set; }
        public string Date { get; set; }
        public string? Time { get; set; }
        public string Numbers { get; set; }
    }
}