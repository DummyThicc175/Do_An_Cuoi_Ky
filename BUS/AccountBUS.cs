using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DAL.MODELS;

namespace BUS
{
    public enum LoginStatus
    {
        Success,
        UserNotFound,
        WrongPassword,
        Inactive,
        Error
    }

    public class AccountBUS
    {
        private static byte[] HexStringToBytes(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return null;
                if (hex.Length % 2 != 0) return null;
                var len = hex.Length / 2;
                var bytes = new byte[len];
                for (int i = 0; i < len; i++)
                {
                    bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                }
                return bytes;
            }
            catch
            {
                return null;
            }
        }

        // Compute SHA256 of byte input and return hex string
        private static string Sha256Hex(byte[] input)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(input);
                var sb = new StringBuilder();
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // Canonical ComputeHash used for initial sample DB: UTF8(salt + password)
        public static string ComputeHashCanonical(string salt, string password)
        {
            var bytes = Encoding.UTF8.GetBytes((salt ?? string.Empty) + (password ?? string.Empty));
            return Sha256Hex(bytes);
        }

        // Update sample accounts that use the known default salt to the canonical hash
        // Only update when PassWordHash is null or empty to avoid overwriting real passwords.
        public void EnsureDefaultHashedPassword(string defaultSalt = "A1B2C3D4E5", string defaultPlainPassword = "123456")
        {
            using (var db = new QLQAContextDB())
            {
                // Only consider rows that have the default salt AND no password hash set
                var accounts = db.Accounts.Where(a => a.Salt == defaultSalt && (string.IsNullOrEmpty(a.PassWordHash) || a.PassWordHash.Trim() == string.Empty)).ToList();
                if (!accounts.Any()) return;

                var realHash = ComputeHashCanonical(defaultSalt, defaultPlainPassword);
                var changed = false;
                foreach (var acc in accounts)
                {
                    if (!string.Equals(acc.PassWordHash, realHash, StringComparison.OrdinalIgnoreCase))
                    {
                        acc.PassWordHash = realHash;
                        changed = true;
                    }
                }

                if (changed) db.SaveChanges();
            }
        }

        // ComputeHash flexible
        private static string ComputeHash(string salt, string password, Encoding encoding, bool saltIsHexBytes = false, bool saltFirst = true)
        {
            byte[] saltBytes = null;
            if (saltIsHexBytes && !string.IsNullOrEmpty(salt))
            {
                saltBytes = HexStringToBytes(salt);
            }

            var passBytes = (encoding ?? Encoding.UTF8).GetBytes(password ?? string.Empty);

            byte[] input;
            if (saltBytes != null)
            {
                input = new byte[saltBytes.Length + passBytes.Length];
                if (saltFirst)
                {
                    Buffer.BlockCopy(saltBytes, 0, input, 0, saltBytes.Length);
                    Buffer.BlockCopy(passBytes, 0, input, saltBytes.Length, passBytes.Length);
                }
                else
                {
                    Buffer.BlockCopy(passBytes, 0, input, 0, passBytes.Length);
                    Buffer.BlockCopy(saltBytes, 0, input, passBytes.Length, saltBytes.Length);
                }
            }
            else
            {
                // use salt as text with specified encoding
                var saltText = salt ?? string.Empty;
                var saltTextBytes = (encoding ?? Encoding.UTF8).GetBytes(saltText);
                input = new byte[saltTextBytes.Length + passBytes.Length];
                if (saltFirst)
                {
                    Buffer.BlockCopy(saltTextBytes, 0, input, 0, saltTextBytes.Length);
                    Buffer.BlockCopy(passBytes, 0, input, saltTextBytes.Length, passBytes.Length);
                }
                else
                {
                    Buffer.BlockCopy(passBytes, 0, input, 0, passBytes.Length);
                    Buffer.BlockCopy(saltTextBytes, 0, input, passBytes.Length, saltTextBytes.Length);
                }
            }

            return Sha256Hex(input);
        }

        // Verify password against account using same variants as Login
        private bool VerifyPassword(Account account, string password)
        {
            if (account == null) return false;
            var encodings = new Encoding[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode, Encoding.ASCII, Encoding.UTF32 };
            var saltVariants = new[] { account.Salt ?? string.Empty, (account.Salt ?? string.Empty).Trim(), (account.Salt ?? string.Empty).ToUpperInvariant(), (account.Salt ?? string.Empty).ToLowerInvariant() };

            foreach (var enc in encodings)
            {
                foreach (var saltVar in saltVariants.Distinct())
                {
                    var try1 = ComputeHash(saltVar, password, enc, saltIsHexBytes: true, saltFirst: true);
                    var try2 = ComputeHash(saltVar, password, enc, saltIsHexBytes: true, saltFirst: false);
                    var try3 = ComputeHash(saltVar, password, enc, saltIsHexBytes: false, saltFirst: true);
                    var try4 = ComputeHash(saltVar, password, enc, saltIsHexBytes: false, saltFirst: false);
                    var tryPassOnly = Sha256Hex(enc.GetBytes(password));
                    var double1 = Sha256Hex(Encoding.UTF8.GetBytes(try3));
                    var double2 = Sha256Hex(Encoding.UTF8.GetBytes(try4));

                    var candidates = new[] { try1, try2, try3, try4, tryPassOnly, double1, double2 };
                    if (candidates.Any(c => string.Equals(c, account.PassWordHash, StringComparison.OrdinalIgnoreCase)))
                        return true;
                }
            }

            return false;
        }

        // New method: check login status with reason
        public LoginStatus CheckLogin(string userName, string password, out Account account)
        {
            account = null;
            if (string.IsNullOrWhiteSpace(userName)) return LoginStatus.UserNotFound;

            using (var db = new QLQAContextDB())
            {
                var acc = db.Accounts.FirstOrDefault(a => a.UserName == userName);
                if (acc == null) return LoginStatus.UserNotFound;
                if (!acc.IsActive) return LoginStatus.Inactive;

                // ensure sample accounts updated (only affects accounts that had empty hash)
                try { EnsureDefaultHashedPassword(); } catch { }

                if (VerifyPassword(acc, password))
                {
                    account = acc;
                    // update last login
                    acc.LastLogin = DateTime.Now;
                    db.SaveChanges();
                    return LoginStatus.Success;
                }

                return LoginStatus.WrongPassword;
            }
        }

        // Try login, return Account on success or null on failure (keeps previous behavior)
        public DAL.MODELS.Account Login(string userName, string password)
        {
            Account acc;
            var status = CheckLogin(userName, password, out acc);
            if (status == LoginStatus.Success) return acc;
            return null;
        }

        // Diagnostic helper
        public string DiagnoseLogin(string userName, string password)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return "Username rỗng.";
            if (string.IsNullOrEmpty(password))
                return "Password rỗng.";

            using (var db = new QLQAContextDB())
            {
                var account = db.Accounts.FirstOrDefault(a => a.UserName == userName);
                if (account == null)
                    return "Không tìm thấy user trong DB (UserName không khớp).";

                if (!account.IsActive)
                    return "Tài khoản tồn tại nhưng đang bị khóa (IsActive = false).";

                if (string.IsNullOrEmpty(account.Salt) || string.IsNullOrEmpty(account.PassWordHash))
                    return "Salt hoặc PassWordHash trong DB rỗng.";

                var sb = new StringBuilder();
                sb.AppendLine("Stored: " + account.PassWordHash);
                sb.AppendLine("Salt (raw): '" + account.Salt + "'");

                var encodings = new (string name, Encoding enc)[] {
                    ("UTF8", Encoding.UTF8),
                    ("Unicode(UTF-16LE)", Encoding.Unicode),
                    ("BigEndianUnicode(UTF-16BE)", Encoding.BigEndianUnicode),
                    ("ASCII", Encoding.ASCII),
                    ("UTF32", Encoding.UTF32)
                };

                foreach (var e in encodings)
                {
                    sb.AppendLine($"--- Encoding: {e.name} ---");
                    var s = account.Salt ?? string.Empty;
                    var sTrim = s.Trim();
                    var sUp = s.ToUpperInvariant();
                    var sLow = s.ToLowerInvariant();
                    var variants = new[] { s, sTrim, sUp, sLow }.Distinct();
                    foreach (var v in variants)
                    {
                        sb.AppendLine($"salt='{v}' hexBytes+saltFirst: " + ComputeHash(v, password, e.enc, saltIsHexBytes: true, saltFirst: true));
                        sb.AppendLine($"salt='{v}' hexBytes+passFirst: " + ComputeHash(v, password, e.enc, saltIsHexBytes: true, saltFirst: false));
                        sb.AppendLine($"salt='{v}' text+saltFirst: " + ComputeHash(v, password, e.enc, saltIsHexBytes: false, saltFirst: true));
                        sb.AppendLine($"salt='{v}' text+passFirst: " + ComputeHash(v, password, e.enc, saltIsHexBytes: false, saltFirst: false));
                    }
                }

                sb.AppendLine("Computed SHA256(password) with UTF8: " + Sha256Hex(Encoding.UTF8.GetBytes(password)));
                sb.AppendLine("Computed SHA256(password) with Unicode: " + Sha256Hex(Encoding.Unicode.GetBytes(password)));

                var saltLooksHex = HexStringToBytes(account.Salt) != null;
                sb.AppendLine("SaltLooksLikeHex: " + saltLooksHex);

                return sb.ToString();
            }
        }
    }
}
