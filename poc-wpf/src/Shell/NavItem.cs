namespace PzlEv.Shell;

/// <summary>Pozycja menu – tworzona z IModule (Key, NavLabel, NavBadge).</summary>
public sealed record NavItem(string Key, string Label, string? Badge);
