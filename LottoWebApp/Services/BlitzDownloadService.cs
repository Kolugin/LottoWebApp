using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LottoWebApp.Services
{
    public class BlitzDownloadService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BlitzDownloadService> _logger;

        public BlitzDownloadService(IServiceProvider serviceProvider, ILogger<BlitzDownloadService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Служба загрузки БЛИЦ запущена.");

            // Запускаем задачу с повторением
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Проверяем, время ли загружать (например, 4:40, 4:45, 4:50, ...)
                    var now = DateTime.Now;
                    var minutes = now.Minute;
                    var seconds = now.Second;

                    // Загружаем, если минута заканчивается на 0 или 5 (например, 40, 45) и секунды = 0
                    if ((minutes % 5 == 0) && seconds == 35)
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var downloader = scope.ServiceProvider.GetRequiredService<LotteryDataDownloader>();
                        await downloader.DownloadBlitzResultsAsync();
                    }

                    // Ждем 1 секунду перед следующей проверкой
                    await Task.Delay(1000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в службе загрузки БЛИЦ.");
                    // Ждем 5 секунд перед повторной попыткой
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("Служба загрузки БЛИЦ остановлена.");
        }
    }
}