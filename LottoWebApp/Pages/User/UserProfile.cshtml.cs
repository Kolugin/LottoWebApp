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

namespace LottoWebApp.Pages.User
{
    [Authorize]
    public class UserProfileModel : PageModel
    {
        private readonly LottoDbContext _context;

        public UserProfileModel(LottoDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [BindProperty]
        public ChangePasswordInputModel ChangePasswordInput { get; set; } = new();

        // Свойства для отображения в View (вне формы редактирования)
        public int Id { get; set; }
        public string Login { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public bool Activity { get; set; }
        public DateTime? LastLogin { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Логин обязателен")]
            [StringLength(255, ErrorMessage = "Логин должен быть не более 255 символов")]
            public string Login { get; set; } = "";

            [Required(ErrorMessage = "Email обязателен")]
            [EmailAddress(ErrorMessage = "Неверный формат email")]
            [StringLength(255, ErrorMessage = "Email должен быть не более 255 символов")]
            public string Email { get; set; } = "";

            [Phone(ErrorMessage = "Неверный формат телефона")]
            [StringLength(100, ErrorMessage = "Телефон должен быть не более 100 символов")]
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

        // -------------------------------------------------------
        // GET: Загрузка профиля и РАСШИФРОВКА ВСЕХ данных
        // -------------------------------------------------------
        public async Task<IActionResult> OnGetAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToPage("/Auth/Login");

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToPage("/Auth/Login");

            // Получаем ключ шифрования пользователя
            var encryptionKeyRecord = await _context.UserEncryptionKeys.FirstOrDefaultAsync(k => k.UserId == user.Id);
            if (encryptionKeyRecord == null)
            {
                return NotFound("Ошибка безопасности: Ключ шифрования пользователя не найден.");
            }

            string key = encryptionKeyRecord.EncryptionKey;
            string iv = encryptionKeyRecord.IV;

            Id = user.Id;
            Activity = user.Activity;
            LastLogin = user.LastLogin;

            // === РАСШИФРОВКА ДАННЫХ ИЗ БД ===
            // Считаем, что в БД поля Login и Email уже зашифрованы, поэтому расшифровываем их для показа
            try
            {
                Login = CryptoHelper.Decrypt(user.Login, key, iv);
                Email = CryptoHelper.Decrypt(user.Email, key, iv);
                Phone = !string.IsNullOrEmpty(user.Phone) ? CryptoHelper.Decrypt(user.Phone, key, iv) : null;
            }
            catch
            {

                Login = "Error Decrypting";
                Email = "Error Decrypting";
            }
            Input.Login = Login;
            Input.Email = Email;
            Input.Phone = Phone;

            return Page();
        }

        // -------------------------------------------------------
        // POST: Обновление профиля и ШИФРОВАНИЕ ВСЕХ данных
        // -------------------------------------------------------
        public async Task<IActionResult> OnPostUpdateProfileAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToPage("/Auth/Login");

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToPage("/Auth/Login");

            // Получаем ключи для шифрования
            var encryptionKeyRecord = await _context.UserEncryptionKeys.FirstOrDefaultAsync(k => k.UserId == user.Id);
            if (encryptionKeyRecord == null)
            {
                ModelState.AddModelError("", "Ключ шифрования не найден.");
                return Page();
            }

            string key = encryptionKeyRecord.EncryptionKey;
            string iv = encryptionKeyRecord.IV;
            user.Login = CryptoHelper.Encrypt(Input.Login, key, iv);
            user.Email = CryptoHelper.Encrypt(Input.Email, key, iv);

            user.Phone = !string.IsNullOrEmpty(Input.Phone)
                ? CryptoHelper.Encrypt(Input.Phone, key, iv)
                : null;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Профиль успешно обновлён и зашифрован!";
            return RedirectToPage();
        }

        // -------------------------------------------------------
        // POST: Смена пароля
        // -------------------------------------------------------
        public async Task<IActionResult> OnPostChangePasswordAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToPage("/Auth/Login");

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToPage("/Auth/Login");

            if (!ModelState.IsValid) return Page();

            var encryptionKeyRecord = await _context.UserEncryptionKeys.FirstOrDefaultAsync(k => k.UserId == user.Id);
            if (encryptionKeyRecord == null)
            {
                ModelState.AddModelError("", "Ключ шифрования не найден.");
                return Page();
            }

            string key = encryptionKeyRecord.EncryptionKey;
            string iv = encryptionKeyRecord.IV;

            // Расшифровываем текущий пароль из БД для сравнения
            string decryptedDbPassword = CryptoHelper.Decrypt(user.Password, key, iv);

            if (decryptedDbPassword != ChangePasswordInput.OldPassword)
            {
                ModelState.AddModelError("ChangePasswordInput.OldPassword", "Старый пароль неверен.");
                return Page();
            }

            // Шифруем новый пароль
            string encryptedNewPassword = CryptoHelper.Encrypt(ChangePasswordInput.NewPassword, key, iv);
            user.Password = encryptedNewPassword;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Пароль успешно изменён!";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAccountAsync()
        {
            var userId = GetCurrentUserId();
            if (userId == null) return RedirectToPage("/Auth/Login");

            var user = await _context.Users.FindAsync(userId.Value);
            if (user == null) return RedirectToPage("/Auth/Login");

            var encryptionKeyRecord = await _context.UserEncryptionKeys.FirstOrDefaultAsync(k => k.UserId == user.Id);

            if (encryptionKeyRecord != null)
            {
                _context.UserEncryptionKeys.Remove(encryptionKeyRecord);
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            await HttpContext.SignOutAsync();
            HttpContext.Session.Clear();

            return RedirectToPage("/Index");
        }

        private int? GetCurrentUserId()
        {
            var claimUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(claimUserId, out int userId))
            {
                return userId;
            }
            return null;
        }
    }
}