namespace PzlEv.Shared.Models.PzlProd;

/// <summary>Grupa raportowa z PZLPROD.LOG.WBS_DIC (docs/zrodla-danych.md, rozdz. 5.2): klucz Z_PROJECT + Z_GRP, opis i kategorie.</summary>
public sealed record P1sGroup(string Project, string Grouping, string Description, string Category, string CategoryGroup, string Info);
