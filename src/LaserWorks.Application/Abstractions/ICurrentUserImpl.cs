using LaserWorks.Domain.Enums;

namespace LaserWorks.Application.Abstractions;

/// <summary>Mutable session holder used by the desktop app and tests.</summary>
public sealed class UserSession : ICurrentUser
{
    public long? UserId { get; private set; }
    public string Username { get; private set; } = "system";
    public string FullName { get; private set; } = "System";
    public UserRole? Role { get; private set; }
    public DateTime? LoggedInAt { get; private set; }
    public bool IsAuthenticated => UserId.HasValue;

    public event EventHandler? Changed;

    public void SignIn(long userId, string username, string fullName, UserRole role, DateTime at)
    {
        UserId = userId;
        Username = username;
        FullName = fullName;
        Role = role;
        LoggedInAt = at;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        UserId = null;
        Username = "system";
        FullName = "System";
        Role = null;
        LoggedInAt = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
