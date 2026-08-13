using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LottoWebApp.Pages.Auth
{
    public class RegisterModel : PageModel
    {
        private readonly LottoDbContext _context;

        public RegisterModel(LottoDbContext context)
        {
            _context = context;
        }

        [BindProperty] public string Login { get; set; } = string.Empty;
        [BindProperty] public string Email { get; set; } = string.Empty;
        [BindProperty] public string Password { get; set; } = string.Empty;
        [BindProperty] public string ConfirmPassword { get; set; } = string.Empty;
        [BindProperty] public string? Phone { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            if (Password != ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "Пароли не совпадают.");
                return Page();
            }

            // Генерация криптостойких ключей (AES-256 CBC)
            var (key, iv) = CryptoHelper.GenerateKeyAndIV();

            // Шифрование пользовательских данных
            var encryptedUser = new Models.User
            {
                Login = CryptoHelper.Encrypt(Login, key, iv),
                Email = CryptoHelper.Encrypt(Email, key, iv),
                Phone = CryptoHelper.Encrypt(Phone ?? "", key, iv),
                Password = CryptoHelper.Encrypt(Password, key, iv),
                Activity = false,
                LastLogin = null
            };

            _context.Users.Add(encryptedUser);
            await _context.SaveChangesAsync(); // теперь user.Id заполнен

            // Сохранение ключей в отдельную таблицу
            var userKey = new UserEncryptionKey
            {
                UserId = encryptedUser.Id,
                EncryptionKey = key,
                IV = iv
            };

            _context.UserEncryptionKeys.Add(userKey);
            await _context.SaveChangesAsync();

            return RedirectToPage("/Auth/Login");
        }
    }
}
