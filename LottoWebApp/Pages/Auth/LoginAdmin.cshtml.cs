using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AdminModel = LottoWebApp.Models.Admin;

namespace LottoWebApp.Pages.Auth
{
    public class LoginAdminModel : PageModel
    {
        private readonly LottoDbContext _context;

        public LoginAdminModel(LottoDbContext context)
        {
            _context = context;
        }

        [BindProperty] public string Login { get; set; }
        [BindProperty] public string Password { get; set; }
        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            // Проверка порта: разрешаем только 6000/6001 (HTTP/HTTPS)
            if (Request.Host.Port != 6000 && Request.Host.Port != 6001)
            {
                return new ContentResult
                {
                    Content = "Доступ запрещён.",
                    ContentType = "text/html; charset=utf-8",
                    StatusCode = 403
                };
            }

            if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Введите логин и пароль.";
                return Page();
            }

            // Загружаем всех админов с их ключами в память
            var admins = await _context.Admins
                .Include(a => a.EncryptionKey)
                .ToListAsync();

            // Фильтруем админа по расшифрованному логину
            AdminModel admin = admins.FirstOrDefault(a =>
            {
                if (a.EncryptionKey == null) return false;

                try
                {
                    var decryptedLogin = CryptoHelper.Decrypt(a.Login, a.EncryptionKey.EncryptionKey, a.EncryptionKey.IV);
                    return decryptedLogin == Login;
                }
                catch
                {
                    return false;
                }
            });

            if (admin == null)
            {
                ErrorMessage = "Неверный логин или пароль.";
                return Page();
            }

            // Проверка пароля
            try
            {
                var decryptedPassword = CryptoHelper.Decrypt(admin.Password, admin.EncryptionKey.EncryptionKey, admin.EncryptionKey.IV);
                if (decryptedPassword != Password)
                {
                    ErrorMessage = "Неверный логин или пароль.";
                    return Page();
                }
            }
            catch
            {
                ErrorMessage = "Ошибка при проверке пароля.";
                return Page();
            }

            // Создаём claims для Cookie-аутентификации
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, Login),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Email, CryptoHelper.Decrypt(admin.Email, admin.EncryptionKey.EncryptionKey, admin.EncryptionKey.IV))
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(claimsIdentity);

            // Входим через Cookie
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // Обновляем активность
            admin.Activity = true;
            admin.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return RedirectToPage("/Admin/Dashboard");
        }
    }
}
