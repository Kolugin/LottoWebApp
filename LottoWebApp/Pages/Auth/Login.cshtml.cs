using LottoWebApp.Data;
using LottoWebApp.Models;
using LottoWebApp.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace LottoWebApp.Pages.Auth
{
    public class LoginModel : PageModel
    {
        private readonly LottoDbContext _context;

        public LoginModel(LottoDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public string Login { get; set; }

        [BindProperty]
        public string Password { get; set; }

        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrWhiteSpace(Password))
            {
                ModelState.AddModelError(string.Empty, "Введите логин и пароль.");
                return Page();
            }

            // Загружаем пользователей вместе с ключами шифрования
            var users = await _context.Users
                .Include(u => u.EncryptionKey)
                .ToListAsync();

            Models.User? matchedUser = null;
            string? keyBase64 = null;
            string? ivBase64 = null;

            // Поиск нужного пользователя
            foreach (var user in users)
            {
                if (user.EncryptionKey == null)
                    continue;

                try
                {
                    keyBase64 = user.EncryptionKey.EncryptionKey;
                    ivBase64 = user.EncryptionKey.IV;

                    var decryptedLogin = CryptoHelper.Decrypt(user.Login, keyBase64, ivBase64);

                    if (decryptedLogin == Login)
                    {
                        matchedUser = user;
                        break;
                    }
                }
                catch
                {
                    continue;
                }
            }

            if (matchedUser == null)
            {
                ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
                return Page();
            }

            // Проверка пароля
            try
            {
                var decryptedPassword = CryptoHelper.Decrypt(matchedUser.Password, keyBase64!, ivBase64!);

                if (decryptedPassword != Password)
                {
                    ModelState.AddModelError(string.Empty, "Неверный логин или пароль.");
                    return Page();
                }
            }
            catch
            {
                ModelState.AddModelError(string.Empty, "Ошибка при проверке пароля.");
                return Page();
            }

            // Успешный вход — создаем ClaimsPrincipal
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, matchedUser.Id.ToString()), // UserId
                new Claim(ClaimTypes.Name, Login),                                // Login
                new Claim(ClaimTypes.Email, CryptoHelper.Decrypt(matchedUser.Email, keyBase64!, ivBase64!)) // Email
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            // Настройки cookie
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,               // сохранять после закрытия браузера
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
                AllowRefresh = true
            };

            // Сохраняем куки
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProperties);


            // Обновляем активность и дату входа
            matchedUser.Activity = true;
            matchedUser.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Перенаправление на страницу UserMainMenu
            return RedirectToPage("/User/UserMainMenu");
        }
    }
}
