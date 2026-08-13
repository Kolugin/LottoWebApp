using System;

namespace LottoWebApp.Models
{
    public class UserEncryptionKey
    {
        public int Id { get; set; }                  // PRIMARY KEY
        public int UserId { get; set; }              // FOREIGN KEY на Users(Id)
        public string EncryptionKey { get; set; }    // NVARCHAR(512) NOT NULL
        public string IV { get; set; }               // NVARCHAR(256) NOT NULL

        // Навигационное свойство на пользователя
        public User User { get; set; }
    }
}
