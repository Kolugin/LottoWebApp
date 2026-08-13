using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LottoWebApp.Data;
using Microsoft.EntityFrameworkCore; 
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LottoWebApp.Services
{
    public class KenoDownloadService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<KenoDownloadService> _logger;

        public KenoDownloadService(IServiceProvider serviceProvider, ILogger<KenoDownloadService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Служба загрузки Кено запущена.");

            await CheckAndLoadIfNotToday(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Вычисляем время следующего запуска (21:30)
                    var now = DateTime.Now;
                    var nextRunTime = new DateTime(now.Year, now.Month, now.Day, 21, 30, 0);

                    // Если 21:30 уже прошло сегодня, ждем до 21:30 завтра
                    if (nextRunTime <= now)
                    {
                        nextRunTime = nextRunTime.AddDays(1); // 21:30:00 завтра
                    }

                    var delay = nextRunTime - now;
                    _logger.LogInformation($"Следующая загрузка Кено запланирована на {nextRunTime:yyyy-MM-dd HH:mm:ss}. Ожидание {delay.TotalSeconds} секунд.");

                    // Ждем до следующего времени запуска, учитывая cancellation token
                    if (delay.TotalMilliseconds > 0)
                    {
                        await Task.Delay(delay, stoppingToken);
                    }

                    // Выполняем загрузку
                    using var scope = _serviceProvider.CreateScope();
                    var downloader = scope.ServiceProvider.GetRequiredService<LotteryDataDownloader>();
                    await downloader.DownloadKenoResultsAsync(); // Проверяем и загружаем новый тираж

                    _logger.LogInformation($"Загрузка Кено выполнена в {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
                }
                catch (TaskCanceledException)
                {
                    // Task.Delay был прерван через cancellation token - выходим из цикла
                    _logger.LogInformation("Ожидание загрузки Кено прервано.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в службе загрузки Кено.");
                    // Ждем 60 минут перед следующей попыткой цикла (ожидания или загрузки)
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.LogInformation("Ожидание после ошибки загрузки Кено прервано.");
                        break;
                    }
                }
            }

            _logger.LogInformation("Служба загрузки Кено остановлена.");
        }

        /// <summary>
        /// Проверяет дату последнего тиража в БД. Если дата не сегодня, запускает загрузку.
        /// </summary>
        private async Task CheckAndLoadIfNotToday(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Проверка даты последнего тиража Кено при запуске службы.");

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LottoDbContext>();

            // Получаем дату последнего тиража из БД
            DateTime? lastDrawDate = null;
            if (context.LotteryKenoBY.Any())
            {
                // Предполагаем, что в модели есть свойство Date типа string в формате "dd.MM.yyyy"
                var lastDrawDateString = context.LotteryKenoBY
                    .OrderByDescending(d => d.Draw) 
                    .Select(d => d.Date) // Выбираем только дату
                    .FirstOrDefault(); // Берем первую (последнюю) запись

                if (DateTime.TryParseExact(lastDrawDateString, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                {
                    lastDrawDate = parsedDate.Date;
                }
            }

            var today = DateTime.Today;

            if (!lastDrawDate.HasValue || lastDrawDate.Value < today)
            {
                _logger.LogInformation($"Дата последнего тиража ({lastDrawDate?.ToString("dd.MM.yyyy") ?? "отсутствует"}) не совпадает с сегодняшней ({today:dd.MM.yyyy}). Выполняется загрузка...");
                var downloader = scope.ServiceProvider.GetRequiredService<LotteryDataDownloader>();
                await downloader.DownloadKenoResultsAsync();
                _logger.LogInformation("Первоначальная загрузка Кено завершена.");
            }
            else
            {
                _logger.LogInformation($"Дата последнего тиража ({lastDrawDate.Value:dd.MM.yyyy}) совпадает с сегодняшней ({today:dd.MM.yyyy}). Загрузка не требуется.");
            }
        }
    }
}