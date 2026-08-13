using LottoWebApp.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LottoWebApp.Services
{
    public class LotteryDataDownloader
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly LottoDbContext _context;
        private readonly ILogger<LotteryDataDownloader> _logger;

        // API URLs
        private const string BLITZ_API_URL = "https://sportpari.by/api/game/4/draw-result/";
        private const string _1224_API_URL = "https://sportpari.by/api/game/16/draw-result/";
        private const string _KENO_API_URL = "https://sportpari.by/api/game/2/draw-result/";
        private const string _536_API_URL = "https://sportpari.by/api/game/1/draw-result/";
        private const string _649_API_URL = "https://sportpari.by/api/game/7/draw-result/";
        private const string _320_API_URL = "https://sportpari.by/api/game/20/draw-result/";
        public LotteryDataDownloader(IHttpClientFactory httpClientFactory, LottoDbContext context, ILogger<LotteryDataDownloader> logger)
        {
            _httpClientFactory = httpClientFactory;
            _context = context;
            _logger = logger;
        }

        public async Task DownloadBlitzResultsAsync()
        {
            _logger.LogInformation("Запуск загрузки тиражей БЛИЦ.");
            await DownloadResultsAsync("BLITZBY", BLITZ_API_URL);
        }

        public async Task Download1224ResultsAsync()
        {
            _logger.LogInformation("Запуск загрузки тиражей 12/24.");
            await DownloadResultsAsync("1224BY", _1224_API_URL);
        }

        public async Task DownloadKenoResultsAsync()
        {
            _logger.LogInformation("Запуск загрузки тиражей КЕНО.");
            await DownloadResultsAsync("KENOBY", _KENO_API_URL);
        }

        public async Task Download536ResultsAsync()
        {
            _logger.LogInformation("Запуск загрузки тиражей 5/36.");
            await DownloadResultsAsync("536BY", _536_API_URL);
        }

        public async Task Download649ResultsAsync()
        {
            _logger.LogInformation("Запуск загрузки тиражей 6/49.");
            await DownloadResultsAsync("649BY", _649_API_URL);
        }

        public async Task Download320ResultsAsync()
        {
            _logger.LogInformation("Запуск загрузки тиражей 3+3.");
            await DownloadResultsAsync("320BY", _320_API_URL);
        }

        // --- Вставка новых строк в БД ---

        private async Task DownloadResultsAsync(string tableName, string apiUrl)
        {
            // Получаем последний тираж из базы данных
            int lastDrawNumber = 0;
            if (tableName == "1224BY")
            {
                lastDrawNumber = _context.Lottery1224BY.Any() ? _context.Lottery1224BY.Max(d => d.Draw) : 0;
            }
            else if (tableName == "BLITZBY")
            {
                lastDrawNumber = _context.LotteryBlitzBY.Any() ? _context.LotteryBlitzBY.Max(d => d.Draw) : 0;
            }
            else if (tableName == "KENOBY")
            {
                lastDrawNumber = _context.LotteryKenoBY.Any() ? _context.LotteryKenoBY.Max(d => d.Draw) : 0;
            }
            else if (tableName == "536BY")
            {
                lastDrawNumber = _context.Lottery536BY.Any() ? _context.Lottery536BY.Max(d => d.Draw) : 0;
            }
            else if (tableName == "649BY")
            {
                lastDrawNumber = _context.Lottery649BY.Any() ? _context.Lottery649BY.Max(d => d.Draw) : 0;
            }
            else if (tableName == "320BY")
            {
                lastDrawNumber = _context.Lottery320BY.Any() ? _context.Lottery320BY.Max(d => d.Draw) : 0;
            }

            int currentDraw = lastDrawNumber + 1;

            using var httpClient = _httpClientFactory.CreateClient();

            while (true)
            {
                var response = await httpClient.GetAsync($"{apiUrl}{currentDraw}");
                // Задержка между запросами для предотвращения перегрузки API
                await Task.Delay(20);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogInformation($"Тираж {currentDraw} для {tableName} не найден. Завершение загрузки.");
                        break; // Завершаем, если тираж не найден
                    }
                    else
                    {
                        _logger.LogError($"Ошибка HTTP при загрузке тиража {currentDraw} для {tableName}: {response.StatusCode}");
                        break; // Пока просто останавливаем при ошибке
                    }
                }

                string json = await response.Content.ReadAsStringAsync();
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;

                    if (!root.TryGetProperty("draw_result", out JsonElement drawResult) ||
                        !drawResult.TryGetProperty("results", out JsonElement results) ||
                        !drawResult.TryGetProperty("expected_date", out JsonElement dateElement))
                    {
                        _logger.LogError($"Ошибка структуры данных JSON для тиража {currentDraw} ({tableName}).");
                        break; // Останавливаем при ошибке структуры
                    }

                    // --- Обработка 'results' для разных типов игр ---
                    int[] mainNumbers;
                    int bonusNumber = 0;

                    if (tableName == "KENOBY")
                    {
                        mainNumbers = results.EnumerateArray().Select(i => i.GetInt32()).ToArray();
                    }
                    else if (tableName == "320BY" || tableName == "536BY")
                    {
                        // Flatten: объединяем все вложенные массивы в один плоский список (как в вашем рабочем примере)
                        // Например: [[8,9,16], [3,12,19]] -> [8, 9, 16, 3, 12, 19]
                        var list = new List<int>();
                        foreach (var set in results.EnumerateArray())
                        {
                            if (set.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var num in set.EnumerateArray())
                                {
                                    list.Add(num.GetInt32());
                                }
                            }
                        }
                        mainNumbers = list.ToArray();
                    }
                    else
                    {
                        // БЛИЦ, 12/24, 6/49: берем первый массив как основной, второй (если есть) как бонус
                        var sets = results.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Array).ToList();
                        mainNumbers = sets.Count > 0 ? sets[0].EnumerateArray().Select(i => i.GetInt32()).ToArray() : Array.Empty<int>();

                        if (tableName == "BLITZBY" && sets.Count > 1 && sets[1].EnumerateArray().Any())
                        {
                            bonusNumber = sets[1].EnumerateArray().First().GetInt32();
                        }
                    }

                    if (mainNumbers.Length == 0)
                    {
                        _logger.LogError($"Основные числа отсутствуют в тираже {currentDraw} ({tableName}).");
                        break;
                    }

                    // --- Парсинг даты ---
                    if (!DateTime.TryParse(dateElement.GetString(), out DateTime drawDateTime))
                    {
                        _logger.LogError($"Невозможно распарсить дату в тираже {currentDraw} ({tableName}).");
                        break;
                    }

                    // --- Вставка в базу данных ---
                    if (tableName == "BLITZBY")
                    {
                        var blitzDraw = new Models.LotteryBlitzBY
                        {
                            Draw = currentDraw,
                            Date = drawDateTime.ToString("dd.MM.yyyy"),
                            Time = drawDateTime.ToString("HH:mm"),
                            B1 = mainNumbers.Length > 0 ? mainNumbers[0] : 0,
                            B2 = mainNumbers.Length > 1 ? mainNumbers[1] : 0,
                            B3 = mainNumbers.Length > 2 ? mainNumbers[2] : 0,
                            B4 = mainNumbers.Length > 3 ? mainNumbers[3] : 0,
                            B5 = mainNumbers.Length > 4 ? mainNumbers[4] : 0,
                            B6 = mainNumbers.Length > 5 ? mainNumbers[5] : 0,
                            B7 = mainNumbers.Length > 6 ? mainNumbers[6] : 0,
                            B8 = mainNumbers.Length > 7 ? mainNumbers[7] : 0,
                            BB = bonusNumber
                        };
                        _context.LotteryBlitzBY.Add(blitzDraw);
                    }
                    else if (tableName == "1224BY")
                    {
                        var twelveDraw = new Models.Lottery1224BY
                        {
                            Draw = currentDraw,
                            Date = drawDateTime.ToString("dd.MM.yyyy"),
                            Time = drawDateTime.ToString("HH:mm"),
                            B1 = mainNumbers.Length > 0 ? mainNumbers[0] : 0,
                            B2 = mainNumbers.Length > 1 ? mainNumbers[1] : 0,
                            B3 = mainNumbers.Length > 2 ? mainNumbers[2] : 0,
                            B4 = mainNumbers.Length > 3 ? mainNumbers[3] : 0,
                            B5 = mainNumbers.Length > 4 ? mainNumbers[4] : 0,
                            B6 = mainNumbers.Length > 5 ? mainNumbers[5] : 0,
                            B7 = mainNumbers.Length > 6 ? mainNumbers[6] : 0,
                            B8 = mainNumbers.Length > 7 ? mainNumbers[7] : 0,
                            B9 = mainNumbers.Length > 8 ? mainNumbers[8] : 0,
                            B10 = mainNumbers.Length > 9 ? mainNumbers[9] : 0,
                            B11 = mainNumbers.Length > 10 ? mainNumbers[10] : 0,
                            B12 = mainNumbers.Length > 11 ? mainNumbers[11] : 0
                        };
                        _context.Lottery1224BY.Add(twelveDraw);
                    }
                    else if (tableName == "536BY")
                    {
                        var _536Draw = new Models.Lottery536BY
                        {
                            Draw = currentDraw,
                            Date = drawDateTime.ToString("dd.MM.yyyy"),
                            B1 = mainNumbers.Length > 0 ? mainNumbers[0] : 0,
                            B2 = mainNumbers.Length > 1 ? mainNumbers[1] : 0,
                            B3 = mainNumbers.Length > 2 ? mainNumbers[2] : 0,
                            B4 = mainNumbers.Length > 3 ? mainNumbers[3] : 0,
                            B5 = mainNumbers.Length > 4 ? mainNumbers[4] : 0,
                            B6 = mainNumbers.Length > 5 ? mainNumbers[5] : 0
                        };
                        _context.Lottery536BY.Add(_536Draw);
                    }
                    else if (tableName == "649BY")
                    {
                        var _649Draw = new Models.Lottery649BY
                        {
                            Draw = currentDraw,
                            Date = drawDateTime.ToString("dd.MM.yyyy"),
                            B1 = mainNumbers.Length > 0 ? mainNumbers[0] : 0,
                            B2 = mainNumbers.Length > 1 ? mainNumbers[1] : 0,
                            B3 = mainNumbers.Length > 2 ? mainNumbers[2] : 0,
                            B4 = mainNumbers.Length > 3 ? mainNumbers[3] : 0,
                            B5 = mainNumbers.Length > 4 ? mainNumbers[4] : 0,
                            B6 = mainNumbers.Length > 5 ? mainNumbers[5] : 0
                        };
                        _context.Lottery649BY.Add(_649Draw);
                    }
                    else if (tableName == "KENOBY")
                    {
                        var KenoDraw = new Models.LotteryKenoBY
                        {
                            Draw = currentDraw,
                            Date = drawDateTime.ToString("dd.MM.yyyy"),
                            B1 = mainNumbers.Length > 0 ? mainNumbers[0] : 0,
                            B2 = mainNumbers.Length > 1 ? mainNumbers[1] : 0,
                            B3 = mainNumbers.Length > 2 ? mainNumbers[2] : 0,
                            B4 = mainNumbers.Length > 3 ? mainNumbers[3] : 0,
                            B5 = mainNumbers.Length > 4 ? mainNumbers[4] : 0,
                            B6 = mainNumbers.Length > 5 ? mainNumbers[5] : 0,
                            B7 = mainNumbers.Length > 6 ? mainNumbers[6] : 0,
                            B8 = mainNumbers.Length > 7 ? mainNumbers[7] : 0,
                            B9 = mainNumbers.Length > 8 ? mainNumbers[8] : 0,
                            B10 = mainNumbers.Length > 9 ? mainNumbers[9] : 0,
                            B11 = mainNumbers.Length > 10 ? mainNumbers[10] : 0,
                            B12 = mainNumbers.Length > 11 ? mainNumbers[11] : 0,
                            B13 = mainNumbers.Length > 12 ? mainNumbers[12] : 0,
                            B14 = mainNumbers.Length > 13 ? mainNumbers[13] : 0,
                            B15 = mainNumbers.Length > 14 ? mainNumbers[14] : 0,
                            B16 = mainNumbers.Length > 15 ? mainNumbers[15] : 0,
                            B17 = mainNumbers.Length > 16 ? mainNumbers[16] : 0,
                            B18 = mainNumbers.Length > 17 ? mainNumbers[17] : 0,
                            B19 = mainNumbers.Length > 18 ? mainNumbers[18] : 0,
                            B20 = mainNumbers.Length > 19 ? mainNumbers[19] : 0
                        };
                        _context.LotteryKenoBY.Add(KenoDraw);
                    }
                    else if (tableName == "320BY")
                    {
                        var _320Draw = new Models.Lottery320BY
                        {
                            Draw = currentDraw,
                            Date = drawDateTime.ToString("dd.MM.yyyy"),
                            Time = drawDateTime.ToString("HH:mm"),
                            B11 = mainNumbers.Length > 0 ? mainNumbers[0] : 0,
                            B12 = mainNumbers.Length > 1 ? mainNumbers[1] : 0,
                            B13 = mainNumbers.Length > 2 ? mainNumbers[2] : 0,
                            B21 = mainNumbers.Length > 3 ? mainNumbers[3] : 0, // Теперь здесь корректно будет 4-е число
                            B22 = mainNumbers.Length > 4 ? mainNumbers[4] : 0, // 5-е число
                            B23 = mainNumbers.Length > 5 ? mainNumbers[5] : 0  // 6-е число
                        };
                        _context.Lottery320BY.Add(_320Draw);
                    }

                    await _context.SaveChangesAsync(); // Сохраняем изменения в БД
                    _logger.LogInformation($"Тираж {currentDraw} для {tableName} успешно загружен и сохранен.");

                    currentDraw++;
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, $"Ошибка при парсинге JSON для тиража {currentDraw} ({tableName}).");
                    break; // Останавливаем при ошибке парсинга
                }
            }
        }
    }
}