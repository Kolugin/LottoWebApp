using System;

namespace LottoWebApp.Models
{
    public class Admin
    {
        public int Id { get; set; }                  // PRIMARY KEY
        public string Login { get; set; }            // NVARCHAR(255) NOT NULL
        public string Password { get; set; }         // NVARCHAR(512) NOT NULL
        public string Email { get; set; }            // NVARCHAR(255) NOT NULL
        public string? Phone { get; set; }           // NVARCHAR(100) NULL
        public bool Activity { get; set; }           // BIT NOT NULL DEFAULT 0
        public DateTime? LastLogin { get; set; }     // DATETIME2 NULL

        // Навигационное свойство для ключа шифрования
        public AdminEncryptionKey? EncryptionKey { get; set; }
    }
}
