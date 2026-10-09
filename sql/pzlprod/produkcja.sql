/* PZL-EV – odczyt danych produkcyjnych projektu z PZLPROD (tylko odczyt; docs/zrodla-danych.md, rozdz. 6).
   Wzorowane na raporcie produkcyjnym S70MR: filtr programu (Z_OPIS, SERNR_LO) zastąpiony zakresem P1S projektu.
   Wbudowane w aplikację (PzlEv.PzlProd.produkcja.sql) – wykonuje SqlPzlProdSource.Production.

   Wejście (aplikacja zakłada i wypełnia w tej samej sesji połączenia, bez praw zapisu w PZLPROD):
     #pspnr     (PSPNR BIGINT)                                   – elementy P1S zakresu projektu (LOG.WBS)
     #rule      (Code, PSPNR, SWBS, CPLGR, ARBPL)                – elementy wirtualne P1S (słownik projektu „Elementy wirtualne P1S”):
                                                                   operacja elementu PSPNR zgodna z regułą (NULL = dowolne) należy
                                                                   do elementu wirtualnego Code (kilka reguł – pierwsza wg Code)
     #djk       (Prefix, Share)                                  – udział godzin jakości DJK wg prefiksu grupy stanowisk (IPT)
                                                                   (słownik „Wskaźniki DJK”; najdłuższy pasujący prefiks)
     #delivered (Status)                                         – statusy vAPD liczone jako dostarczone („Parametry produkcji”)
     @months    INT                                              – okno produktywności IPT w miesiącach („Parametry produkcji”)
     @divideZClo BIT                                             – wartość materiałów ÷ (1 + Z_CLO) („Parametry produkcji”)
   Nazwy widoków: $(vAHDD), $(vAHDD_PL_CPI), $(vAPD) – podstawiane z konfiguracji (pzl-ev.json, PzlProd, schemat LOG).

   Wynik 1 – godziny i daty po elemencie (PSPNR; VirtualCode – element wirtualny, inaczej NULL):
     AC = CATS; BAC = TECH ÷ produktywność IPT + DJK × TECH; EV = TECH_PON ÷ produktywność IPT + DJK × TECH_PON;
     produktywność IPT = Σ CzTechPon ÷ Σ CzRzecz z vAHDD_PL_CPI w oknie @months (brak – 1);
     Actual Start = MIN(GSTRI) (element wirtualny – MIN(DATA_REAL) jego operacji);
     Actual Finish = MAX(LTRMI) (wirtualny – MAX(DATA_REAL)), gdy wszystkie operacje mają STAT = 'DONE', inaczej NULL.
   Wynik 2 – materiały po PSPNR (vAPD): wartość dostarczona Σ NETWR_USD [÷ (1 + Z_CLO)] dla statusów #delivered (USD). */

WITH Produktywnosc AS (
    SELECT IPT, SUM(CzTechPon) / SUM(CzRzecz) AS Factor
    FROM $(vAHDD_PL_CPI)
    WHERE DataZakonczeniaOperacji >= DATEADD(MONTH, -@months, GETDATE())
    GROUP BY IPT
    HAVING SUM(CzRzecz) > 0 AND SUM(CzTechPon) > 0),
A AS (
    SELECT p.PSPNR, a.IPT, a.CATS, a.TECH, a.TECH_PON, a.STAT,
           TRY_CONVERT(DATE, a.GSTRI) AS GSTRI, TRY_CONVERT(DATE, a.LTRMI) AS LTRMI, TRY_CONVERT(DATE, a.DATA_REAL) AS DATA_REAL,
           r.Code AS VirtualCode
    FROM $(vAHDD) a
    JOIN #pspnr p ON p.PSPNR = TRY_CONVERT(BIGINT, a.PSPNR)
    OUTER APPLY (
        SELECT TOP (1) x.Code
        FROM #rule x
        WHERE x.PSPNR = p.PSPNR
          AND (x.SWBS IS NULL OR x.SWBS = a.SWBS)
          AND (x.CPLGR IS NULL OR x.CPLGR = a.CPLGR)
          AND (x.ARBPL IS NULL OR x.ARBPL = a.ARBPL)
        ORDER BY x.Code) r)
SELECT CAST(A.PSPNR AS NVARCHAR(50)) AS Pspnr, A.VirtualCode,
       CAST(SUM(A.CATS) AS DECIMAL(28,8)) AS AcHours,
       CAST(SUM(A.TECH / ISNULL(P.Factor, 1)) + SUM(ISNULL(K.Share, 0) * A.TECH) AS DECIMAL(28,8)) AS BacHours,
       CAST(SUM(A.TECH_PON / ISNULL(P.Factor, 1)) + SUM(ISNULL(K.Share, 0) * A.TECH_PON) AS DECIMAL(28,8)) AS EvHours,
       MIN(CASE WHEN A.VirtualCode IS NULL THEN A.GSTRI ELSE A.DATA_REAL END) AS ActualStart,
       CASE WHEN SUM(CASE WHEN A.STAT = 'DONE' THEN 0 ELSE 1 END) = 0
            THEN MAX(CASE WHEN A.VirtualCode IS NULL THEN A.LTRMI ELSE A.DATA_REAL END) END AS ActualFinish
FROM A
LEFT JOIN Produktywnosc P ON P.IPT = A.IPT
OUTER APPLY (
    SELECT TOP (1) d.Share
    FROM #djk d
    WHERE LEFT(A.IPT, LEN(d.Prefix)) = d.Prefix
    ORDER BY LEN(d.Prefix) DESC) K
GROUP BY A.PSPNR, A.VirtualCode;

SELECT CAST(p.PSPNR AS NVARCHAR(50)) AS Pspnr,
       CAST(SUM(CASE WHEN s.Status IS NOT NULL
                     THEN v.NETWR_USD / IIF(@divideZClo = 1, 1 + ISNULL(v.Z_CLO, 0), 1) ELSE 0 END) AS DECIMAL(28,8)) AS Delivered
FROM $(vAPD) v
JOIN #pspnr p ON p.PSPNR = TRY_CONVERT(BIGINT, v.PSPNR)
LEFT JOIN #delivered s ON s.Status = v.STATUS
GROUP BY p.PSPNR;
