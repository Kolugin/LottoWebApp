using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace LottoWebApp.Pages.Admin
{ 
    public class AdminProfileModel : PageModel
    {
        private readonly LottoDbContext _context;

        public AdminProfileModel(LottoDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [BindProperty]
        public ChangePasswordInputModel ChangePasswordInput { get; set; } = new();

        // Свойства для отображения в интерфейсе
        public int Id { get; set; }
        public string Login { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public bool Activity { get; set; }
        public DateTime? LastLogin { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Логин обязателен")]
            public string Login { get; set; } = "";

            [Required(ErrorMessage = "Email обязателен")]
            [EmailAddress(ErrorMessage = "Неверный формат email")]
            public string Email { get; set; } = "";

            [Phone(ErrorMessage = "Неверный формат телефона")]
            public string? Phone { get; set; }
        }

        public class ChangePasswordInputModel
        {
            [Required(ErrorMessage = "Старый пароль обязателен")]
            public string OldPassword { get; set; } = "";

            [Required(ErrorMessage = "Новый пароль обязателен")]
            [StringLength(100, MinimumLength = 6, ErrorMessage = "Пароль должен быть не менее 6 символов")]
            public string NewPassword { get; set; } = "";

            [Required(ErrorMessage = "Подтверждение пароля обязательно")]
            [Compare("NewPassword", ErrorMessage = "Пароли не совпадают")]
            public string ConfirmNewPassword { get; set; } = "";
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var adminId = GetCurrentAdminId();
            if (adminId == null) return RedirectToPage("/Auth/LoginAdmin");

            var admin = await _context.Admins
                .Include(a => a.EncryptionKey)
                .FirstOrDefaultAsync(a => a.Id == adminId.Value);

            if (admin == null) return RedirectToPage("/Auth/LoginAdmin");

            if (admin.EncryptionKey == null)
            {
                return NotFound("Критическая ошибка: Ключ шифрования администратора не найден.");
            }

            // Временные переменные в ОЗУ для ключей
            string key = admin.EncryptionKey.EncryptionKey;
            string iv = admin.EncryptionKey.IV;

            Id = admin.Id;
            Activity = admin.Activity;
            LastLogin = admin.LastLogin;

            try
            {
                // Расшифровка данных во временные переменные
                Login = CryptoHelper.Decrypt(admin.Login, key, iv);

                Email = !string.IsNullOrEmpty(admin.Email) ? CryptoHelper.Decrypt(admin.Email, key, iv) : "";
                Phone = !string.IsNullOrEmpty(admin.Phone) ? CryptoHelper.Decrypt(admin.Phone, key, iv) : null;
            }
            catch (Exception)
            {
                Login = "[Ошибка расшифровки]";
                Email = "[Ошибка расшифровки]";
            }

            Input.Login = Login;
            Input.Email = Email;
            Input.Phone = Phone;

            return Page();
        }

        public async Task<IActionResult> OnPostUpdateProfileAsync()
        {
            var adminId = GetCurrentAdminId();
            if (adminId == null) return RedirectToPage("/Auth/LoginAdmin");

            var admin = await _context.Admins
                .Include(a => a.EncryptionKey)
                .FirstOrDefaultAsync(a => a.Id == adminId.Value);

            if (admin == null || admin.EncryptionKey == null) return Page();

            // Сохраняем ключи в ОЗУ
            string key = admin.EncryptionKey.EncryptionKey;
            string iv = admin.EncryptionKey.IV;

            // Шифруем введенные данные и сохраняем
            admin.Login = CryptoHelper.Encrypt(Input.Login, key, iv);

            if (!string.IsNullOrEmpty(Input.Email))
                admin.Email = CryptoHelper.Encrypt(Input.Email, key, iv);

            if (!string.IsNullOrEmpty(Input.Phone))
                admin.Phone = CryptoHelper.Encrypt(Input.Phone, key, iv);

            _context.Admins.Update(admin);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Профиль администратора обновлен.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostChangePasswordAsync()
        {
            var adminId = GetCurrentAdminId();
            if (adminId == null) return RedirectToPage("/Auth/LoginAdmin");

            var admin = await _context.Admins
                .Include(a => a.EncryptionKey)
                .FirstOrDefaultAsync(a => a.Id == adminId.Value);

            if (admin == null || admin.EncryptionKey == null) return Page();

            if (!ModelState.IsValid) return Page();

            string key = admin.EncryptionKey.EncryptionKey;
            string iv = admin.EncryptionKey.IV;

            // Секретная переменная в ОЗУ для проверки пароля
            string currentDbPassword = CryptoHelper.Decrypt(admin.Password, key, iv);

            if (currentDbPassword != ChangePasswordInput.OldPassword)
            {
                ModelState.AddModelError("ChangePasswordInput.OldPassword", "Старый пароль введен неверно.");
                return Page();
            }

            // Шифруем новый пароль
            admin.Password = CryptoHelper.Encrypt(ChangePasswordInput.NewPassword, key, iv);

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Пароль администратора успешно изменен!";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAccountAsync()
        {
            var adminId = GetCurrentAdminId();
            if (adminId == null) return RedirectToPage("/Auth/LoginAdmin");

            var admin = await _context.Admins
                .Include(a => a.EncryptionKey)
                .FirstOrDefaultAsync(a => a.Id == adminId.Value);

            if (admin != null)
            {
                // Удаляем ключ шифрования
                if (admin.EncryptionKey != null)
                {
                    _context.AdminEncryptionKeys.Remove(admin.EncryptionKey);
                }

                _context.Admins.Remove(admin);
                await _context.SaveChangesAsync();
            }

            await HttpContext.SignOutAsync();
            return RedirectToPage("/Index");
        }
        private int? GetCurrentAdminId()
        {
            // Пытаемся получить ID из клаймов (ClaimTypes.NameIdentifier)
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(claim, out int id)) return id;
            return null;
        }
    }
}