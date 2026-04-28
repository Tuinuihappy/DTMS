using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AMR.DeliveryPlanning.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/token", async (LoginRequest request, AuthDbContext authDb, IConfiguration config) =>
        {
            var user = await authDb.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive);
            if (user == null || !user.VerifyPassword(request.Password))
                return Results.Unauthorized();

            var jwtSettings = config.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim("tenant_id", user.TenantId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwtSettings.Issuer,
                audience: jwtSettings.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(jwtSettings.ExpirationMinutes),
                signingCredentials: credentials);

            return Results.Ok(new
            {
                token = new JwtSecurityTokenHandler().WriteToken(token),
                expiresAt = token.ValidTo
            });
        })
        .WithTags("Auth")
        .AllowAnonymous();

        // POST /api/auth/register — Register a new user (admin only)
        app.MapPost("/api/auth/register", async (RegisterRequest request, AuthDbContext authDb, HttpContext httpContext) =>
        {
            if (await authDb.Users.AnyAsync(u => u.Username == request.Username))
                return Results.BadRequest("Username already exists.");

            // Tenant is supplied via X-Tenant-Id header; defaults to system tenant if absent.
            if (!Guid.TryParse(httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault(), out var regTenantId))
                regTenantId = AppUser.SystemTenantId;

            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                TenantId = regTenantId,
                Username = request.Username,
                PasswordHash = AppUser.HashPassword(request.Password),
                Role = request.Role ?? "Operator",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await authDb.Users.AddAsync(user);
            await authDb.SaveChangesAsync();

            return Results.Created($"/api/auth/users/{user.Id}", new { user.Id, user.Username, user.Role });
        })
        .WithTags("Auth")
        .RequireAuthorization();
    }

    /// <summary>
    /// Seeds the default admin user if no users exist.
    /// </summary>
    public static async Task SeedDefaultUserAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new AppUser
            {
                Id = Guid.NewGuid(),
                TenantId = AppUser.SystemTenantId,
                Username = "admin",
                PasswordHash = AppUser.HashPassword("admin123"),
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}

public record LoginRequest(string Username, string Password);
public record RegisterRequest(string Username, string Password, string? Role);
