using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;

namespace LottoWebApp.Pages.User
{
    [Authorize] // Защищает страницу от неавторизованных пользователей
    public class UserMainMenuModel : PageModel
    {
        // Имя текущего пользователя
        public string? UserName { get; private set; }

        public void OnGet()
        {
            UserName = User.Identity?.Name ?? "Неизвестный";
        }

        /// <summary>
        /// Переход к странице редактирования профиля.
        /// </summary>
        public async Task<IActionResult> OnGetEditProfile()
        {
            return RedirectToPage("/User/UserProfile");
        }


        /// <summary>
        /// Выход из учетной записи (очистка cookie и редирект).
        /// </summary>
        public async Task<IActionResult> OnGetLogoutAsync()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToPage("/Auth/Login"); // Правильный маршрут к странице входа
        }
    }
}