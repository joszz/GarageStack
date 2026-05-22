namespace GarageStack.Core.Models;

public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<UserRefreshToken> RefreshTokens { get; set; } = [];
}
