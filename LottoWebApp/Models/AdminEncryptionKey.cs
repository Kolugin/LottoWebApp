using System;

namespace LottoWebApp.Models
{
    public class AdminEncryptionKey
    {
        public int Id { get; set; }                  // PRIMARY KEY
        public int AdminId { get; set; }             // FOREIGN KEY на Admins(Id)
        public string EncryptionKey { get; set; }    // NVARCHAR(512) NOT NULL
        public string IV { get; set; }               // NVARCHAR(256) NOT NULL

        // Навигационное свойство на администратора
        public Admin Admin { get; set; }
    }
}
