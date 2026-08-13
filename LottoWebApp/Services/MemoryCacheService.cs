using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading.Tasks;

namespace LottoWebApp.Services
{
    public interface ICacheService
    {
        /// <summary>
        /// Получает объект из кеша или создаёт его через фабрику, если его нет.
        /// </summary>
        /// <typeparam name="T">Тип кешируемого объекта</typeparam>
        /// <param name="key">Уникальный ключ кеша</param>
        /// <param name="factory">Функция для создания объекта при его отсутствии</param>
        /// <param name="expiration">Время жизни кеша</param>
        /// <returns>Кешируемый объект</returns>
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);
        // Удаляет объект из кеша по ключу
        Task RemoveAsync(string key);
    }

    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly MemoryCacheEntryOptions _defaultOptions;
        public MemoryCacheService(IMemoryCache cache)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _defaultOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            };
        }
        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            if (_cache.TryGetValue(key, out T cachedValue))
            {
                return cachedValue;
            }
            // Генерация значения через фабрику
            T value = await factory();
            // Настройки времени жизни
            var options = expiration.HasValue
                ? new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = expiration.Value }
                : _defaultOptions;
            _cache.Set(key, value, options);
            return value;
        }
        public Task RemoveAsync(string key)
        {
            if (key != null)
            {
                _cache.Remove(key);
            }
            return Task.CompletedTask;
        }
    }
}
