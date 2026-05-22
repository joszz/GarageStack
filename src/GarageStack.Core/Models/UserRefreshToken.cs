namespace GarageStack.Core.Models;

public class UserRefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool Revoked { get; set; }
    public AppUser User { get; set; } = null!;
}
