namespace PzlEv.Shared.Models.Db;

/// <summary>Wpis dziennika zdarzeń – meta.Zdarzenie (docs/model-danych.md, rozdz. 5).</summary>
public sealed record JournalEntry(
    long Id,
    DateTimeOffset At,
    string User,
    string Area,
    string? Scope,
    string Message);
