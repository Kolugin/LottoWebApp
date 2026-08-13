using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LottoWebApp.Services
{
    public class _320DownloadService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<_320DownloadService> _logger;

        public _320DownloadService(IServiceProvider serviceProvider, ILogger<_320DownloadService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Служба загрузки 3+3 запущена.");

            // Хранит время последнего успешного запуска для защиты от дублирующих срабатываний
            DateTime lastRunTime = DateTime.MinValue;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;

                    if (now.Hour >= 12 && now.Hour <= 21 && now.Minute == 1 && (now - lastRunTime).TotalMinutes > 30)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var downloader = scope.ServiceProvider.GetRequiredService<LotteryDataDownloader>();
                        await downloader.Download320ResultsAsync();

                        lastRunTime = DateTime.Now;
                    }

                    // Вычисляем точное время до начала следующей минуты, чтобы не нагружать CPU ежесекундными проверками
                    now = DateTime.Now;
                    var delay = TimeSpan.FromSeconds(60 - now.Second) + TimeSpan.FromMilliseconds(100);
                    await Task.Delay(delay, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в службе загрузки 3+3.");
                    // При ошибке ждем 5 секунд перед следующей итерацией
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("Служба загрузки 3+3 остановлена.");
        }
    }
}