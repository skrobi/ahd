"""Warstwa bazy danych MVP: rejestr importów, plików i surowych wierszy.

Obsługiwane adresy bazy:
    sqlite:///C:/AHD_TEST/ahd_mvp.sqlite          – plik lokalny (domyślnie w MVP)
    mssql://SERWER/BAZA                            – MS SQL, logowanie kontem Windows (AD)
    mssql://SERWER/BAZA?driver=ODBC+Driver+18+for+SQL+Server&encrypt=no

Zapytania DML używają parametrów ``?`` – wspólnych dla sqlite3 i pyodbc. Schemat dla MS SQL
tworzy administrator skryptem ``sql/mssql/001_etap1_import.sql``; dla SQLite tworzony jest
automatycznie.
"""

from __future__ import annotations

import datetime as dt
import json
import sqlite3
from pathlib import Path
from typing import Any, Iterable, Iterator
from urllib.parse import parse_qs, unquote, urlsplit

SQLITE_DDL = """
CREATE TABLE IF NOT EXISTS meta_ImportBatch (
    BatchId        TEXT PRIMARY KEY,
    Zrodlo         TEXT NOT NULL,
    Uzytkownik     TEXT NOT NULL,
    Komputer       TEXT,
    WersjaAplikacji TEXT,
    Start          TEXT NOT NULL,
    Koniec         TEXT,
    PlikowWidzianych   INTEGER,
    PlikowZaimportowanych INTEGER,
    PlikowPominietych  INTEGER,
    Bledow         INTEGER,
    Status         TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS meta_SourceFile (
    Sha256         TEXT PRIMARY KEY,
    NazwaPliku     TEXT NOT NULL,
    Zrodlo         TEXT NOT NULL,
    Rozmiar        INTEGER NOT NULL,
    ZmodyfikowanyWZrodle TEXT,
    SciezkaLandingZone TEXT NOT NULL,
    BatchId        TEXT NOT NULL REFERENCES meta_ImportBatch(BatchId),
    Zaimportowano  TEXT NOT NULL,
    Uzytkownik     TEXT NOT NULL,
    TypPliku       TEXT,
    Arkusz         TEXT,
    Kodowanie      TEXT,
    Separator      TEXT,
    Kolumny        TEXT,
    SygnaturaKolumn TEXT,
    TypRaportu     TEXT,
    LiczbaWierszy  INTEGER
);
CREATE TABLE IF NOT EXISTS meta_SourceFileSeen (
    BatchId        TEXT NOT NULL REFERENCES meta_ImportBatch(BatchId),
    NazwaPliku     TEXT NOT NULL,
    Zrodlo         TEXT NOT NULL,
    Rozmiar        INTEGER NOT NULL,
    ZmodyfikowanyWZrodle TEXT,
    Sha256         TEXT,
    Decyzja        TEXT NOT NULL,
    Opis           TEXT
);
CREATE INDEX IF NOT EXISTS IX_Seen_Zrodlo ON meta_SourceFileSeen(Zrodlo, Rozmiar, ZmodyfikowanyWZrodle);
CREATE TABLE IF NOT EXISTS stg_RawRow (
    Sha256         TEXT NOT NULL REFERENCES meta_SourceFile(Sha256),
    NrWiersza      INTEGER NOT NULL,
    Dane           TEXT NOT NULL,
    PRIMARY KEY (Sha256, NrWiersza)
);
"""

TABLES = {
    "sqlite": {"batch": "meta_ImportBatch", "file": "meta_SourceFile", "seen": "meta_SourceFileSeen", "row": "stg_RawRow"},
    "mssql": {"batch": "meta.ImportBatch", "file": "meta.SourceFile", "seen": "meta.SourceFileSeen", "row": "stg.RawRow"},
}

# Decyzje, po których plik o tych samych metadanych można pominąć bez pobierania.
KNOWN_DECISIONS = ("zaimportowany", "duplikat", "pominiety (metadane)")


class DatabaseError(Exception):
    pass


class DuplicateFile(Exception):
    """Plik o tym hashu został w międzyczasie zaimportowany (np. przez inną osobę)."""


def _json_value(v: Any) -> Any:
    if isinstance(v, (dt.datetime, dt.date, dt.time)):
        return v.isoformat()
    return v


class Database:
    def __init__(self, url: str) -> None:
        self.url = url
        parts = urlsplit(url)
        if parts.scheme == "sqlite":
            # sqlite:///C:/x.db -> C:/x.db ; sqlite:////tmp/x.db -> /tmp/x.db
            path = unquote(parts.path)
            if len(path) > 2 and path[0] == "/" and path[2] == ":":
                path = path[1:]
            Path(path).parent.mkdir(parents=True, exist_ok=True)
            self.dialect = "sqlite"
            self.conn = sqlite3.connect(path)
            self.conn.executescript(SQLITE_DDL)
            self.conn.commit()
        elif parts.scheme == "mssql":
            try:
                import pyodbc
            except ImportError as exc:
                raise DatabaseError("Połączenie z MS SQL wymaga pakietu pyodbc (pip install pyodbc).") from exc
            q = {k: v[0] for k, v in parse_qs(parts.query).items()}
            driver = q.get("driver", "ODBC Driver 18 for SQL Server")
            conn_str = (
                f"DRIVER={{{driver}}};SERVER={parts.netloc};DATABASE={parts.path.strip('/')};"
                f"Trusted_Connection=yes;Encrypt={q.get('encrypt', 'yes')};"
                f"TrustServerCertificate={q.get('trustservercertificate', 'yes')}"
            )
            self.dialect = "mssql"
            try:
                self.conn = pyodbc.connect(conn_str, autocommit=False)
            except pyodbc.Error as exc:
                raise DatabaseError(f"Nie można połączyć się z bazą {parts.netloc}/{parts.path.strip('/')}: {exc}") from exc
            cur = self.conn.cursor()
            cur.execute("SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id "
                        "WHERE s.name+'.'+t.name IN ('meta.ImportBatch','meta.SourceFile','meta.SourceFileSeen','stg.RawRow')")
            if cur.fetchone()[0] != 4:
                raise DatabaseError("Brak tabel AHD w bazie – administrator uruchamia sql/mssql/001_etap1_import.sql.")
        else:
            raise DatabaseError(f"Nieobsługiwany adres bazy: {url} (sqlite:///… albo mssql://…)")
        self.t = TABLES[self.dialect]

    # -- pomocnicze -------------------------------------------------------
    def _ts(self, value: dt.datetime | None = None) -> Any:
        value = value or dt.datetime.now()
        value = value.replace(microsecond=value.microsecond // 1000 * 1000)
        return value.isoformat(timespec="milliseconds") if self.dialect == "sqlite" else value

    def _cursor(self) -> Any:
        cur = self.conn.cursor()
        if self.dialect == "mssql":
            cur.fast_executemany = True
        return cur

    def commit(self) -> None:
        self.conn.commit()

    def rollback(self) -> None:
        self.conn.rollback()

    def close(self) -> None:
        self.conn.close()

    # -- import -----------------------------------------------------------
    def start_batch(self, batch_id: str, zrodlo: str, uzytkownik: str, komputer: str, wersja: str) -> None:
        self._cursor().execute(
            f"INSERT INTO {self.t['batch']} (BatchId, Zrodlo, Uzytkownik, Komputer, WersjaAplikacji, Start, Status) "
            "VALUES (?, ?, ?, ?, ?, ?, ?)",
            (batch_id, zrodlo, uzytkownik, komputer, wersja, self._ts(), "w toku"),
        )
        self.commit()

    def finish_batch(self, batch_id: str, widziane: int, zaimportowane: int, pominiete: int, bledy: int) -> None:
        self._cursor().execute(
            f"UPDATE {self.t['batch']} SET Koniec=?, PlikowWidzianych=?, PlikowZaimportowanych=?, "
            "PlikowPominietych=?, Bledow=?, Status=? WHERE BatchId=?",
            (self._ts(), widziane, zaimportowane, pominiete, bledy, "zakonczony" if not bledy else "z bledami", batch_id),
        )
        self.commit()

    def unchanged_by_metadata(self, zrodlo: str, rozmiar: int, zmodyfikowany: str) -> str | None:
        """Hash pliku, jeśli plik o tych samych metadanych był już obsłużony."""
        marks = ",".join("?" for _ in KNOWN_DECISIONS)
        cur = self._cursor()
        cur.execute(
            f"SELECT Sha256 FROM {self.t['seen']} WHERE Zrodlo=? AND Rozmiar=? AND ZmodyfikowanyWZrodle=? "
            f"AND Decyzja IN ({marks}) AND Sha256 IS NOT NULL",
            (zrodlo, rozmiar, zmodyfikowany, *KNOWN_DECISIONS),
        )
        row = cur.fetchone()
        return row[0] if row else None

    def file_by_hash(self, sha: str) -> dict[str, Any] | None:
        cur = self._cursor()
        cur.execute(f"SELECT NazwaPliku, BatchId, Zaimportowano, Uzytkownik FROM {self.t['file']} WHERE Sha256=?", (sha,))
        row = cur.fetchone()
        return dict(zip(("NazwaPliku", "BatchId", "Zaimportowano", "Uzytkownik"), row)) if row else None

    def record_seen(self, batch_id: str, name: str, zrodlo: str, rozmiar: int, zmodyfikowany: str,
                    sha: str | None, decyzja: str, opis: str = "") -> None:
        self._cursor().execute(
            f"INSERT INTO {self.t['seen']} (BatchId, NazwaPliku, Zrodlo, Rozmiar, ZmodyfikowanyWZrodle, Sha256, Decyzja, Opis) "
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            (batch_id, name, zrodlo, rozmiar, zmodyfikowany, sha, decyzja, opis),
        )
        self.commit()

    def import_file(self, meta: dict[str, Any], rows: Iterable[list[Any]], chunk: int = 5000) -> int:
        """Rejestruje plik i ładuje jego wiersze w jednej transakcji. Zwraca liczbę wierszy."""
        cur = self._cursor()
        try:
            cur.execute(
                f"INSERT INTO {self.t['file']} (Sha256, NazwaPliku, Zrodlo, Rozmiar, ZmodyfikowanyWZrodle, "
                "SciezkaLandingZone, BatchId, Zaimportowano, Uzytkownik, TypPliku, Arkusz, Kodowanie, Separator, "
                "Kolumny, SygnaturaKolumn, TypRaportu, LiczbaWierszy) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)",
                (meta["sha256"], meta["plik"], meta["zrodlo"], meta["rozmiar"], meta["zmodyfikowany"],
                 meta["landing"], meta["batch"], self._ts(), meta["uzytkownik"], meta.get("typ"), meta.get("arkusz"),
                 meta.get("kodowanie"), meta.get("separator"), json.dumps(meta.get("kolumny", []), ensure_ascii=False),
                 meta.get("sygnatura"), meta.get("typ_raportu"), None),
            )
        except Exception as exc:
            self.rollback()
            if self.file_by_hash(meta["sha256"]):
                raise DuplicateFile(meta["sha256"]) from exc
            raise
        count = 0
        try:
            buf: list[tuple[str, int, str]] = []
            for count, row in enumerate(rows, 1):
                buf.append((meta["sha256"], count, json.dumps([_json_value(v) for v in row], ensure_ascii=False)))
                if len(buf) >= chunk:
                    cur.executemany(f"INSERT INTO {self.t['row']} (Sha256, NrWiersza, Dane) VALUES (?, ?, ?)", buf)
                    buf.clear()
            if buf:
                cur.executemany(f"INSERT INTO {self.t['row']} (Sha256, NrWiersza, Dane) VALUES (?, ?, ?)", buf)
            cur.execute(f"UPDATE {self.t['file']} SET LiczbaWierszy=? WHERE Sha256=?", (count, meta["sha256"]))
            self.commit()
        except Exception:
            self.rollback()
            raise
        return count

    # -- odczyt -----------------------------------------------------------
    def history(self, limit: int = 50) -> Iterator[tuple[Any, ...]]:
        cur = self._cursor()
        top = f"TOP {int(limit)} " if self.dialect == "mssql" else ""
        tail = "" if self.dialect == "mssql" else f" LIMIT {int(limit)}"
        cur.execute(
            f"SELECT {top}Zaimportowano, Uzytkownik, NazwaPliku, TypRaportu, LiczbaWierszy, Sha256 "
            f"FROM {self.t['file']} ORDER BY Zaimportowano DESC{tail}"
        )
        yield from cur.fetchall()

    def batches(self, limit: int = 20) -> Iterator[tuple[Any, ...]]:
        cur = self._cursor()
        top = f"TOP {int(limit)} " if self.dialect == "mssql" else ""
        tail = "" if self.dialect == "mssql" else f" LIMIT {int(limit)}"
        cur.execute(
            f"SELECT {top}Start, Uzytkownik, PlikowWidzianych, PlikowZaimportowanych, PlikowPominietych, Bledow, Status "
            f"FROM {self.t['batch']} ORDER BY Start DESC{tail}"
        )
        yield from cur.fetchall()

    def count_rows(self, sha: str) -> int:
        cur = self._cursor()
        cur.execute(f"SELECT COUNT(*) FROM {self.t['row']} WHERE Sha256=?", (sha,))
        return cur.fetchone()[0]
