using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Threading.Tasks;

namespace LottoWebApp.Pages.Auth
{
    public class RegisterAdminModel : PageModel
    {
        private readonly LottoDbContext _context;

        public RegisterAdminModel(LottoDbContext context)
        {
            _context = context;
        }

        [BindProperty] public string Login { get; set; }
        [BindProperty] public string Email { get; set; }
        [BindProperty] public string Password { get; set; }
        [BindProperty] public string ConfirmPassword { get; set; }
        [BindProperty] public string? Phone { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            if (Password != ConfirmPassword)
            {
                ModelState.AddModelError("ConfirmPassword", "Пароли не совпадают.");
                return Page();
            }

            // Генерация ключей шифрования
            var (key, iv) = CryptoHelper.GenerateKeyAndIV();

            // Шифруем данные
            var encryptedLogin = CryptoHelper.Encrypt(Login, key, iv);
            var encryptedEmail = CryptoHelper.Encrypt(Email, key, iv);
            var encryptedPhone = CryptoHelper.Encrypt(Phone ?? "", key, iv);
            var encryptedPassword = CryptoHelper.Encrypt(Password, key, iv);

            // Создаём администратора
            var admin = new Models.Admin
            {
                Login = encryptedLogin,
                Email = encryptedEmail,
                Phone = encryptedPhone,
                Password = encryptedPassword,
                Activity = false,
                LastLogin = null
            };

            _context.Admins.Add(admin);
            await _context.SaveChangesAsync();

            // Сохраняем ключи шифрования
            var adminKey = new AdminEncryptionKey
            {
                AdminId = admin.Id,
                EncryptionKey = key,
                IV = iv
            };

            _context.AdminEncryptionKeys.Add(adminKey);
            await _context.SaveChangesAsync();

            return RedirectToPage("/Auth/LoginAdmin");
        }
    }
}
