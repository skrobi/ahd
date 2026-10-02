namespace PzlEv.Modules.Dashboard.Models;

/// <summary>Wpis dziennika zdarzeń.</summary>
public sealed record EventItem(string When, string Where, string Who, string Message);
