namespace Engram.Store.Cloud;

/// <summary>
/// Privacy classification for data. Determines what can be sent to cloud.
/// Public: freely shareable metadata
/// Internal: workspace-level data, not shared
/// Private: raw screen, clipboard, email content — NEVER sent to cloud
/// Sensitive: passwords, tokens, PII — NEVER stored or transmitted
/// </summary>
public enum PrivacyClass
{
    Public = 0,
    Internal = 1,
    Private = 2,
    Sensitive = 3
}
