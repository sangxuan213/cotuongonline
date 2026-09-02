using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using XiangqiOnline.Persistence.Configuration;

namespace XiangqiOnline.Persistence.Services;

public sealed record AccountIdentity(string AccountId, string Email, string DisplayName);
public sealed record AccountOperation(bool Success, string Code, string Message, AccountIdentity? Account = null);
public sealed record PasswordResetIssue(bool ShouldSend, string? Code, string? DisplayName, string Message);

/// <summary>Account storage with PBKDF2 passwords and single-use password reset codes.</summary>
public sealed class AccountService
{
    public const int PasswordIterations = 210_000;
    private readonly string _connectionString;
    private readonly byte[] _resetPepper;

    public AccountService(DatabaseOptions options, string resetPepper)
    {
        _connectionString = options.BuildConnectionString();
        if (string.IsNullOrWhiteSpace(resetPepper) || resetPepper.Length < 16)
            throw new ArgumentException("Reset-code pepper must contain at least 16 characters.", nameof(resetPepper));
        _resetPepper = Encoding.UTF8.GetBytes(resetPepper);
    }

    public AccountOperation Register(string email, string displayName, string password, DateTimeOffset now)
    {
        email = NormalizeEmail(email);
        displayName = (displayName ?? string.Empty).Trim();
        var validation = Validate(email, displayName, password);
        if (validation is not null) return validation;

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(password, salt, PasswordIterations);
        var id = Guid.NewGuid().ToString("N");
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO accounts(account_id,email,display_name,password_hash,password_salt,password_iterations,is_active,created_at_utc,updated_at_utc)
            VALUES($id,$email,$name,$hash,$salt,$iterations,1,$now,$now);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$name", displayName);
        command.Parameters.Add("$hash", SqliteType.Blob).Value = hash;
        command.Parameters.Add("$salt", SqliteType.Blob).Value = salt;
        command.Parameters.AddWithValue("$iterations", PasswordIterations);
        command.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
        try
        {
            command.ExecuteNonQuery();
            return new(true, "REGISTERED", "Đăng ký thành công.", new(id, email, displayName));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return new(false, "ACCOUNT_EXISTS", "Email hoặc tên hiển thị đã được sử dụng.");
        }
    }

    public AccountOperation Authenticate(string email, string password)
    {
        email = NormalizeEmail(email);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT account_id,email,display_name,password_hash,password_salt,password_iterations,is_active FROM accounts WHERE email=$email;";
        command.Parameters.AddWithValue("$email", email);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return InvalidCredentials();
        var actual = (byte[])reader[3];
        var salt = (byte[])reader[4];
        var iterations = reader.GetInt32(5);
        var candidate = HashPassword(password ?? string.Empty, salt, iterations);
        if (reader.GetInt32(6) != 1 || !CryptographicOperations.FixedTimeEquals(actual, candidate)) return InvalidCredentials();
        return new(true, "AUTHENTICATED", "Đăng nhập thành công.",
            new(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
    }

    public PasswordResetIssue IssuePasswordReset(string email, DateTimeOffset now)
    {
        email = NormalizeEmail(email);
        using var connection = Open();
        using var find = connection.CreateCommand();
        find.CommandText = "SELECT account_id,display_name FROM accounts WHERE email=$email AND is_active=1;";
        find.Parameters.AddWithValue("$email", email);
        using var reader = find.ExecuteReader();
        if (!reader.Read()) return GenericResetIssue();
        var accountId = reader.GetString(0);
        var name = reader.GetString(1);
        reader.Close();

        using var throttle = connection.CreateCommand();
        throttle.CommandText = "SELECT requested_at_utc FROM password_reset_codes WHERE account_id=$id ORDER BY requested_at_utc DESC LIMIT 1;";
        throttle.Parameters.AddWithValue("$id", accountId);
        var lastValue = throttle.ExecuteScalar()?.ToString();
        if (DateTimeOffset.TryParse(lastValue, out var last) && now - last < TimeSpan.FromSeconds(60)) return GenericResetIssue();

        var code = RandomNumberGenerator.GetInt32(100000, 1_000_000).ToString();
        using var transaction = connection.BeginTransaction();
        using var expire = connection.CreateCommand();
        expire.Transaction = transaction;
        expire.CommandText = "UPDATE password_reset_codes SET consumed_at_utc=$now WHERE account_id=$id AND consumed_at_utc IS NULL;";
        expire.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
        expire.Parameters.AddWithValue("$id", accountId);
        expire.ExecuteNonQuery();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO password_reset_codes(reset_id,account_id,code_hash,expires_at_utc,requested_at_utc) VALUES($reset,$id,$hash,$expires,$now);";
        insert.Parameters.AddWithValue("$reset", Guid.NewGuid().ToString("N"));
        insert.Parameters.AddWithValue("$id", accountId);
        insert.Parameters.Add("$hash", SqliteType.Blob).Value = HashResetCode(accountId, code);
        insert.Parameters.AddWithValue("$expires", now.AddMinutes(10).UtcDateTime.ToString("O"));
        insert.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
        insert.ExecuteNonQuery();
        transaction.Commit();
        return new(true, code, name, GenericResetIssue().Message);
    }

    public AccountOperation ResetPassword(string email, string code, string newPassword, DateTimeOffset now)
    {
        email = NormalizeEmail(email);
        if (!IsStrongPassword(newPassword)) return new(false, "WEAK_PASSWORD", PasswordRuleMessage);
        using var connection = Open();
        using var find = connection.CreateCommand();
        find.CommandText = """
            SELECT r.reset_id,r.account_id,r.code_hash,r.expires_at_utc,r.attempt_count
            FROM password_reset_codes r JOIN accounts a ON a.account_id=r.account_id
            WHERE a.email=$email AND r.consumed_at_utc IS NULL ORDER BY r.requested_at_utc DESC LIMIT 1;
            """;
        find.Parameters.AddWithValue("$email", email);
        using var reader = find.ExecuteReader();
        if (!reader.Read()) return InvalidReset();
        var resetId = reader.GetString(0);
        var accountId = reader.GetString(1);
        var expected = (byte[])reader[2];
        var expires = DateTimeOffset.Parse(reader.GetString(3));
        var attempts = reader.GetInt32(4);
        reader.Close();
        var valid = now <= expires && attempts < 5 && code?.Length == 6 &&
                    CryptographicOperations.FixedTimeEquals(expected, HashResetCode(accountId, code));
        if (!valid)
        {
            using var bump = connection.CreateCommand();
            bump.CommandText = "UPDATE password_reset_codes SET attempt_count=attempt_count+1 WHERE reset_id=$id;";
            bump.Parameters.AddWithValue("$id", resetId);
            bump.ExecuteNonQuery();
            return InvalidReset();
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(newPassword, salt, PasswordIterations);
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE accounts SET password_hash=$hash,password_salt=$salt,password_iterations=$iterations,updated_at_utc=$now WHERE account_id=$id;";
        update.Parameters.Add("$hash", SqliteType.Blob).Value = hash;
        update.Parameters.Add("$salt", SqliteType.Blob).Value = salt;
        update.Parameters.AddWithValue("$iterations", PasswordIterations);
        update.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
        update.Parameters.AddWithValue("$id", accountId);
        update.ExecuteNonQuery();
        using var consume = connection.CreateCommand();
        consume.Transaction = transaction;
        consume.CommandText = "UPDATE password_reset_codes SET consumed_at_utc=$now WHERE reset_id=$reset;";
        consume.Parameters.AddWithValue("$now", now.UtcDateTime.ToString("O"));
        consume.Parameters.AddWithValue("$reset", resetId);
        consume.ExecuteNonQuery();
        transaction.Commit();
        return new(true, "PASSWORD_RESET", "Đổi mật khẩu thành công. Bạn có thể đăng nhập ngay.");
    }

    public static bool IsStrongPassword(string? password) => password is { Length: >= 8 and <= 128 }
        && password.Any(character => !char.IsWhiteSpace(character));
    public const string PasswordRuleMessage = "Mật khẩu cần từ 8 đến 128 ký tự.";

    private SqliteConnection Open() { var connection = new SqliteConnection(_connectionString); connection.Open(); return connection; }
    private byte[] HashResetCode(string accountId, string code) => HMACSHA256.HashData(_resetPepper, Encoding.UTF8.GetBytes(accountId + ":" + code));
    private static byte[] HashPassword(string password, byte[] salt, int iterations) => Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
    private static string NormalizeEmail(string? email)
    {
        try
        {
            return new MailAddress((email ?? string.Empty).Trim()).Address.ToLowerInvariant();
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
    private static AccountOperation? Validate(string email, string name, string password)
    {
        if (string.IsNullOrEmpty(email) || email.Length > 254) return new(false, "EMAIL_INVALID", "Email không hợp lệ.");
        if (name.Length is < 1 or > 24 || name.Any(char.IsControl)) return new(false, "DISPLAY_NAME_INVALID", "Tên hiển thị cần từ 1 đến 24 ký tự.");
        return IsStrongPassword(password) ? null : new(false, "WEAK_PASSWORD", PasswordRuleMessage);
    }
    private static AccountOperation InvalidCredentials() => new(false, "INVALID_CREDENTIALS", "Email hoặc mật khẩu không đúng.");
    private static AccountOperation InvalidReset() => new(false, "RESET_INVALID", "Mã không đúng, đã hết hạn hoặc đã được sử dụng.");
    private static PasswordResetIssue GenericResetIssue() => new(false, null, null, "Nếu email tồn tại, mã xác nhận sẽ được gửi trong ít phút.");
}
