using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LottoWebApp.Services
{
    public class _1224DownloadService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<_1224DownloadService> _logger;

        public _1224DownloadService(IServiceProvider serviceProvider, ILogger<_1224DownloadService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Служба загрузки 12/24 запущена.");

            // Запускаем задачу с повторением
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Проверяем, время ли загружать
                    var now = DateTime.Now;
                    var minutes = now.Minute;
                    var seconds = now.Second;

                    // Загружаем, если минута заканчивается на 3 или 8 (например, 43, 48) и секунды = 0
                    if (((minutes + 2) % 5 == 0) && seconds == 30) // Смещение на 3 минуты относительно БЛИЦ
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var downloader = scope.ServiceProvider.GetRequiredService<LotteryDataDownloader>();
                        await downloader.Download1224ResultsAsync();
                    }

                    // Ждем 1 секунду перед следующей проверкой
                    await Task.Delay(1000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка в службе загрузки 12/24.");
                    // Ждем 5 секунд перед повторной попыткой
                    await Task.Delay(5000, stoppingToken);
                }
            }

            _logger.LogInformation("Служба загрузки 12/24 остановлена.");
        }
    }
}