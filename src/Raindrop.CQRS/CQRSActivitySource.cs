using System.Diagnostics;

namespace Raindrop.CQRS;

/// <summary>
/// CQRS OpenTelemetry ActivitySource。
/// </summary>
public static class CQRSActivitySource
{
    /// <summary>
    /// ActivitySource 名称。
    /// </summary>
    public const string Name = "Raindrop.CQRS";

    /// <summary>
    /// 共享的 ActivitySource 实例。
    /// </summary>
    public static readonly ActivitySource Source = new(Name);
}
