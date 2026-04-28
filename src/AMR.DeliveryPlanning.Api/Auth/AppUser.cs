using System.Security.Cryptography;
using System.Text;

namespace AMR.DeliveryPlanning.Api.Auth;

/// <summary>
/// Simple user entity stored in the database for authentication.
/// </summary>
public class AppUser
{
    // Well-known tenant for seeded system/admin accounts.
    public static readonly Guid SystemTenantId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Operator";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public bool VerifyPassword(string password) => PasswordHash == HashPassword(password);
}
