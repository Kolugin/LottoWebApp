using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LottoWebApp.Security
{
    public static class CryptoHelper
    {
        public static (string Key, string IV) GenerateKeyAndIV()
        {
            using var aes = Aes.Create();
            aes.KeySize = 256;
            aes.GenerateKey();
            aes.GenerateIV();
            return (Convert.ToBase64String(aes.Key), Convert.ToBase64String(aes.IV));
        }

        public static string Encrypt(string plainText, string base64Key, string base64IV)
        {
            if (string.IsNullOrEmpty(plainText))
                return string.Empty;

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Convert.FromBase64String(base64Key);
            aes.IV = Convert.FromBase64String(base64IV);

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(plainText);
            }

            return Convert.ToBase64String(ms.ToArray());
        }

        public static string Decrypt(string cipherText, string base64Key, string base64IV)
        {
            if (string.IsNullOrEmpty(cipherText))
                return string.Empty;

            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = Convert.FromBase64String(base64Key);
            aes.IV = Convert.FromBase64String(base64IV);

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(Convert.FromBase64String(cipherText));
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
    }
}
