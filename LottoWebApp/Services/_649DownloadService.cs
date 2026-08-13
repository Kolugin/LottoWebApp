using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LottoWebApp.Data; 
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LottoWebApp.Services
{
    public class _649DownloadService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<_649DownloadService> _logger;

        // Определяем дни недели, в которые нужно запускать
        private static readonly HashSet<DayOfWeek> ScheduledDays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday };

        public _649DownloadService(IServiceProvider serviceProvider, ILogger<_649DownloadService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Служба загрузки 6/49 запущена.");

            // Опционально: Сделать первую проверку/загрузку при старте, если дата не сегодня
            await CheckAndLoadIfNotToday(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Вычисляем время следующего запуска (21:30 в ближайший Пн, Вт, Чт, Сб)
                    var nextRunTime = GetNextScheduledTime();
                    var now = DateTime.Now;
                    var delay = nextRunTime - now;

                    _logger.LogInformation($"Следующая загрузка 6/49 запланирована на {nextRunTime:yyyy-MM-dd HH:mm:ss} ({nextRunTime:dddd}). Ожидание {delay.TotalSeconds} секунд.");

                    // Ждем до следующего времени запуска, учитывая cancellation token
                    if (delay.TotalMilliseconds > 0)
                    {
                        await Task.Delay(delay, stoppingToken);
                    }

                    // Выполняем загрузку
                    using var scope = _serviceProvider.CreateScope();
                    var downloader = scope.ServiceProvider.GetRequiredService<LotteryDataDownloader>();
                    await downloader.Download649ResultsAsync(); // Проверяем и загружаем новый тираж

                    _logger.LogInformation($"Загрузка 6/49 выполнена в {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
                }
                catch (TaskCanceledException)
                {
                    // Task.Delay был прерван через cancellation token - выходим из цикла
                    _logger.LogInformation("Ожидание загрузки 6/49 прервано.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в службе загрузки 6/49.");
                    // Ждем 5 минут перед следующей попыткой цикла (ожидания или загрузки)
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                    }
                    catch (TaskCanceledException)
                    {
                        // Если во время ожидания 5 минут придет отмена
                        _logger.LogInformation("Ожидание после ошибки загрузки 6/49 прервано.");
                        break;
                    }
                }
            }

            _logger.LogInformation("Служба загрузки 6/49 остановлена.");
        }

        /// <summary>
        /// Вычисляет ближайшее время запуска (21:30) в один из запланированных дней недели (Пн, Вт, Чт, Сб).
        /// </summary>
        /// <returns>DateTime следующего запуска.</returns>
        private DateTime GetNextScheduledTime()
        {
            var now = DateTime.Now;
            var scheduledTimeOfDay = new TimeSpan(21, 30, 0); // 21:30:00
            var currentDay = now.DayOfWeek;
            var currentTimeOfDay = now.TimeOfDay;

            // Проверяем, возможен ли запуск *сегодня*
            if (ScheduledDays.Contains(currentDay))
            {
                var todayScheduledTime = now.Date.Add(scheduledTimeOfDay);
                if (currentTimeOfDay < scheduledTimeOfDay)
                {
                    // Если сейчас раньше 21:30 и сегодня подходящий день, возвращаем сегодняшнюю 21:30
                    return todayScheduledTime;
                }
                // Иначе ищем дальше (в будущем)
            }

            // Ищем следующий подходящий день
            var nextDay = currentDay;
            var daysToAdd = 0;
            while (true)
            {
                daysToAdd++;
                nextDay = nextDay == DayOfWeek.Sunday ? DayOfWeek.Monday : (DayOfWeek)(((int)nextDay + 1) % 7);
                if (ScheduledDays.Contains(nextDay))
                {
                    break; // Нашли день
                }
            }

            // Возвращаем 21:30 в найденный день
            return now.Date.AddDays(daysToAdd).Add(scheduledTimeOfDay);
        }

        /// <summary>
        /// Проверяет дату последнего тиража в БД. Если дата не сегодня, запускает загрузку.
        /// </summary>
        private async Task CheckAndLoadIfNotToday(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Проверка даты последнего тиража 6/49 при запуске службы.");

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<LottoDbContext>();

            // Получаем дату последнего тиража из БД
            DateTime? lastDrawDate = null;
            if (context.Lottery649BY.Any())
            {
                // Предполагаем, что в модели есть свойство Date типа string в формате "dd.MM.yyyy"
                var lastDrawDateString = context.Lottery649BY
                    .OrderByDescending(d => d.Draw) // Сортируем по Draw, чтобы получить самый последний
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
                await downloader.Download649ResultsAsync();
                _logger.LogInformation("Первоначальная загрузка 6/49 завершена.");
            }
            else
            {
                _logger.LogInformation($"Дата последнего тиража ({lastDrawDate.Value:dd.MM.yyyy}) совпадает с сегодняшней ({today:dd.MM.yyyy}). Загрузка не требуется.");
            }
        }
    }
}