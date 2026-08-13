using DocumentFormat.OpenXml.Spreadsheet;
using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace LottoWebApp.Pages.Admin
{
    public class DashboardModel : PageModel
    {
        private readonly LottoDbContext _context;

        public DashboardModel(LottoDbContext context)
        {
            _context = context;
        }

        [BindProperty] public List<UserListItem> Users { get; set; } = new();
        [BindProperty] public List<AdminListItem> Admins { get; set; } = new();
        [BindProperty] public SystemStats Stats { get; set; } = new();

        [BindProperty]
        public int SelectedUserId { get; set; }

        [BindProperty]
        public int SelectedAdminId { get; set; }

        // Свойства для форм добавления (должны быть в .cshtml)
        [BindProperty] public string NewLogin { get; set; }
        [BindProperty] public string NewPassword { get; set; }

        public async Task OnGetAsync()
        {
            await LoadUsersData();
            await LoadAdminsData();
            LoadSystemStats();
        }

        // Обработчики для кнопок, "Удалить", "Обновить ключ", "Редактировать мой профиль"
       
        // Обработчик выхода
        public async Task<IActionResult> OnPostLogoutAsync()
        {
            // Получаем текущего админа
            var currentAdminLogin = User.Identity?.Name;

            if (!string.IsNullOrEmpty(currentAdminLogin))
            {
                // Загружаем всех админов с ключами в память
                var admins = await _context.Admins
                    .Include(a => a.EncryptionKey)
                    .ToListAsync(); // <-- Здесь загружаем в память, EF Core больше не нужен

                // Ищем соответствующего администратора через обычный C# код
                var admin = admins.FirstOrDefault(a =>
                    a.EncryptionKey != null &&
                    CryptoHelper.Decrypt(a.Login, a.EncryptionKey.EncryptionKey, a.EncryptionKey.IV) == currentAdminLogin);

                if (admin != null)
                {
                    admin.Activity = false;
                    await _context.SaveChangesAsync();
                }
            }

            // Выход из аутентификации
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Перенаправление на страницу логина
            return RedirectToPage("/Auth/LoginAdmin");
        }
        public async Task<IActionResult> OnPostDeleteUserAsync()
        {
            // 1. Проверяем, выбран ли ID
            if (SelectedUserId <= 0)
            {
                TempData["ErrorMessage"] = "Пользователь не выбран.";
                return RedirectToPage();
            }

            // 2. Загружаем пользователя вместе с его ключом
            var user = await _context.Users
                .Include(u => u.EncryptionKey)
                .FirstOrDefaultAsync(u => u.Id == SelectedUserId);

            if (user == null)
            {
                TempData["ErrorMessage"] = "Пользователь не найден.";
                return RedirectToPage();
            }

            try
            {
                // 3. Удаляем связанный ключ (если нет каскадного удаления в БД)
                if (user.EncryptionKey != null)
                {
                    _context.UserEncryptionKeys.Remove(user.EncryptionKey);
                }

                // 4. Удаляем самого пользователя
                _context.Users.Remove(user);

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Пользователь {user.Login} успешно удален.";
            }
            catch (Exception ex)
            {
                // Логируем ошибку, если что-то пошло не так (например, связи с другими таблицами)
                TempData["ErrorMessage"] = "Ошибка при удалении: " + ex.Message;
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateUserEncryptionKeyAsync()
        {
            // Используем SelectedUserId из привязки радио-кнопки
            int id = SelectedUserId;

            var user = await _context.Users.Include(u => u.EncryptionKey).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null || user.EncryptionKey == null)
            {
                TempData["ErrorMessage"] = "Пользователь не выбран или ключ не найден.";
                return RedirectToPage();
            }

            try
            {
                // ШАГ 1: Сохраняем старые ключи в ОЗУ
                string oldK = user.EncryptionKey.EncryptionKey;
                string oldI = user.EncryptionKey.IV;

                // ШАГ 2: Дешифруем всё во временный буфер (секретные переменные)
                string bufferLogin = CryptoHelper.Decrypt(user.Login, oldK, oldI);
                string bufferPass = CryptoHelper.Decrypt(user.Password, oldK, oldI);
                string bufferEmail = CryptoHelper.Decrypt(user.Email, oldK, oldI);
                string bufferPhone = CryptoHelper.Decrypt(user.Phone, oldK, oldI);

                // ШАГ 3: Создаем новые правильные AES-ключи (Base64)
                var (newK, newI) = CryptoHelper.GenerateKeyAndIV();

                // ШАГ 4: Записываем новые зашифрованные данные обратно в модель
                user.Login = CryptoHelper.Encrypt(bufferLogin, newK, newI);
                user.Password = CryptoHelper.Encrypt(bufferPass, newK, newI);
                user.Email = CryptoHelper.Encrypt(bufferEmail, newK, newI);
                user.Phone = CryptoHelper.Encrypt(bufferPhone, newK, newI);

                user.EncryptionKey.EncryptionKey = newK;
                user.EncryptionKey.IV = newI;

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Ключи пользователя успешно обновлены.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Критическая ошибка при обновлении: " + ex.Message;
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAdminAsync(int selectedAdminId)
        {

            var admin = await _context.Admins
                .Include(a => a.EncryptionKey)
                .FirstOrDefaultAsync(a => a.Id == selectedAdminId);

            if (admin == null) return RedirectToPage();

            // 1. Проверка на "самого себя"
            var currentAdminLogin = User.Identity?.Name;
            try
            {
                var decryptedLogin = CryptoHelper.Decrypt(admin.Login, admin.EncryptionKey.EncryptionKey, admin.EncryptionKey.IV);
                if (decryptedLogin.Trim() == currentAdminLogin) // Добавили Trim()
                {
                    TempData["ErrorMessage"] = "Нельзя удалить самого себя.";
                    return RedirectToPage();
                }
            }
            catch { /* Если дешифровка упала, значит данные уже битые, позволяем удалить */ }

            // 2. Удаление
            if (admin.EncryptionKey != null)
            {
                _context.AdminEncryptionKeys.Remove(admin.EncryptionKey);
            }
            _context.Admins.Remove(admin);

            await _context.SaveChangesAsync();
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateAdminEncryptionKeyAsync()
        {
            int id = SelectedAdminId;
            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Выберите администратора.";
                return RedirectToPage();
            }

            // 1. Извлекаем сущность со всеми связями
            var admin = await _context.Admins.Include(a => a.EncryptionKey).FirstOrDefaultAsync(a => a.Id == id);
            if (admin == null || admin.EncryptionKey == null) return RedirectToPage();

            try
            {
                // --- ШАГ 1: ВРЕМЕННОЕ ХРАНЕНИЕ СТАРЫХ КЛЮЧЕЙ В ОЗУ ---
                string tempOldKey = admin.EncryptionKey.EncryptionKey;
                string tempOldIv = admin.EncryptionKey.IV;

                // --- ШАГ 2: ДЕШИФРОВКА ВО ВРЕМЕННЫЕ ПЕРЕМЕННЫЕ ---
                string secretRawLogin = CryptoHelper.Decrypt(admin.Login, tempOldKey, tempOldIv).Trim();
                string secretRawPassword = CryptoHelper.Decrypt(admin.Password, tempOldKey, tempOldIv).Trim();
                string secretRawEmail = CryptoHelper.Decrypt(admin.Email, tempOldKey, tempOldIv).Trim();
                string secretRawPhone = CryptoHelper.Decrypt(admin.Phone, tempOldKey, tempOldIv).Trim();

                // --- ШАГ 3: ГЕНЕРАЦИЯ НОВЫХ ВАЛИДНЫХ КЛЮЧЕЙ ---
                var (newKey, newIv) = CryptoHelper.GenerateKeyAndIV();

                // --- ШАГ 4: ПЕРЕШИФРОВКА И ПЕРЕЗАПИСЬ ---
                admin.Login = CryptoHelper.Encrypt(secretRawLogin, newKey, newIv);
                admin.Password = CryptoHelper.Encrypt(secretRawPassword, newKey, newIv);
                admin.Email = CryptoHelper.Encrypt(secretRawEmail, newKey, newIv);
                admin.Phone = CryptoHelper.Encrypt(secretRawPhone, newKey, newIv);

                admin.EncryptionKey.EncryptionKey = newKey;
                admin.EncryptionKey.IV = newIv;

                // Сохраняем изменения
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Ключ администратора успешно обновлен.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Ошибка ротации: " + ex.Message;
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEditMyProfileAsync()
        {
            return RedirectToPage("/Admin/AdminProfile");
        }

        private async Task LoadUsersData()
        {
            // Загружаем всех пользователей с их ключами шифрования
            var users = await _context.Users
                .Include(u => u.EncryptionKey) // Подгружаем связанный ключ шифрования
                .ToListAsync();

            Users = new List<UserListItem>();
            foreach (var user in users)
            {
                // Проверяем, есть ли ключ шифрования
                if (user.EncryptionKey == null)
                {
                    // Если ключа нет, мы не можем расшифровать. Можно пропустить или добавить с пустым логином.
                    Users.Add(new UserListItem
                    {
                        Id = user.Id,
                        Login = "[Зашифровано, ключ отсутствует]", // Помечаем, что не расшифровано
                        LastLogin = user.LastLogin,
                        Activity = user.Activity
                    });
                    continue;
                }

                try
                {
                    // Расшифровываем логин, используя ключи из связанной сущности
                    var decryptedLogin = CryptoHelper.Decrypt(user.Login, user.EncryptionKey.EncryptionKey, user.EncryptionKey.IV);

                    Users.Add(new UserListItem
                    {
                        Id = user.Id,
                        Login = decryptedLogin,
                        LastLogin = user.LastLogin,
                        Activity = user.Activity
                    });
                }
                catch (Exception ex)
                {
                    // Если не удалось расшифровать (например, ошибка ключа, поврежденные данные), помечаем ошибку
                    Users.Add(new UserListItem
                    {
                        Id = user.Id,
                        Login = "[Ошибка расшифровки]", // <-- Помечаем ошибку
                        LastLogin = user.LastLogin,
                        Activity = user.Activity
                    });
                    // Логгирование ошибки (если используется логгер)
                    // _logger.LogError(ex, "Ошибка при расшифровке логина пользователя Id={UserId}", user.Id);
                }
            }
        }

        private async Task LoadAdminsData()
        {
            // Загружаем всех администраторов с их ключами шифрования
            var admins = await _context.Admins
                .Include(a => a.EncryptionKey) // Подгружаем связанный ключ шифрования
                .ToListAsync();

            Admins = new List<AdminListItem>();
            foreach (var admin in admins)
            {
                // Проверяем, есть ли ключ шифрования
                if (admin.EncryptionKey == null)
                {
                    // Если ключа нет, мы не можем расшифровать. 
                    Admins.Add(new AdminListItem
                    {
                        Id = admin.Id,
                        Login = "[Зашифровано, ключ отсутствует]", // Помечаем, что не расшифровано
                        LastLogin = admin.LastLogin,
                        Activity = admin.Activity
                    });
                    continue;
                }

                try
                {
                    // Расшифровываем логин, используя ключи из связанной сущности
                    var decryptedLogin = CryptoHelper.Decrypt(admin.Login, admin.EncryptionKey.EncryptionKey, admin.EncryptionKey.IV);

                    Admins.Add(new AdminListItem
                    {
                        Id = admin.Id,
                        Login = decryptedLogin,
                        LastLogin = admin.LastLogin,
                        Activity = admin.Activity
                    });
                }
                catch (Exception ex)
                {
                    // Если не удалось расшифровать (например, ошибка ключа, поврежденные данные), помечаем ошибку
                    Admins.Add(new AdminListItem
                    {
                        Id = admin.Id,
                        Login = "[Ошибка расшифровки]", // <-- Помечаем ошибку
                        LastLogin = admin.LastLogin,
                        Activity = admin.Activity
                    });
                }
            }
        }

        private void LoadSystemStats()
        {
            var memInfo = GC.GetGCMemoryInfo();
            double bytesInGb = 1024.0 * 1024.0 * 1024.0;
            double bytesInMb = 1024.0 * 1024.0;

            // Считываем диски
            var driveC = DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith("C") && d.IsReady);
            var driveD = DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith("D") && d.IsReady);

            // Считываем скорость и CPU через PerformanceCounter
            int cpu = 0;
            int readMbPerSec = 0;
            int writeMbPerSec = 0;

            try
            {
                using var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                using var readCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                using var writeCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

                cpuCounter.NextValue();
                readCounter.NextValue();
                writeCounter.NextValue();

                Thread.Sleep(100); // Даем время для накопления дельты

                cpu = (int)cpuCounter.NextValue();
                readMbPerSec = (int)(readCounter.NextValue() / bytesInMb);
                writeMbPerSec = (int)(writeCounter.NextValue() / bytesInMb);
            }
            catch { /* Обработка отсутствия прав доступа к счетчикам */ }

            Stats = new SystemStats
            {
                CpuUsage = cpu,
                RamUsed = Math.Round(memInfo.MemoryLoadBytes / bytesInGb, 2),
                RamTotal = Math.Round(memInfo.TotalAvailableMemoryBytes / bytesInGb, 2),
                DiskCUsedPercent = driveC != null ? (int)(100 - (driveC.AvailableFreeSpace * 100 / driveC.TotalSize)) : 0,
                DiskDUsedPercent = driveD != null ? (int)(100 - (driveD.AvailableFreeSpace * 100 / driveD.TotalSize)) : 0,
                DiskReadSpeed = readMbPerSec,
                DiskWriteSpeed = writeMbPerSec
            };
        }
    }

    // Классы для передачи данных в представление
    public class UserListItem
    {
        public int Id { get; set; }
        public string Login { get; set; } = string.Empty;
        public DateTime? LastLogin { get; set; }
        public bool Activity { get; set; }
    }

    public class AdminListItem
    {
        public int Id { get; set; }
        public string Login { get; set; } = string.Empty;
        public DateTime? LastLogin { get; set; }
        public bool Activity { get; set; }
    }

    public class SystemStats
    {
        public int CpuUsage { get; set; } // в процентах
        public double RamUsed { get; set; } // в ГБ
        public double RamTotal { get; set; } // в ГБ
        public int DiskCUsedPercent { get; set; } // в процентах
        public int DiskDUsedPercent { get; set; } // в процентах
        public int DiskReadSpeed { get; set; } // в MB/s
        public int DiskWriteSpeed { get; set; } // в MB/s
    }
}