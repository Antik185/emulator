namespace SpacesBrowser.Models;

public enum BrowserKind
{
    Brave,
    Edge,
    Chrome,
    Custom
}

public sealed record BrowserInstall(BrowserKind Kind, string DisplayName, string ExecutablePath)
{
    public bool HasBuiltInFingerprintProtection => Kind == BrowserKind.Brave;
}
