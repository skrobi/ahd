"""Testy etapu 1: pobranie plików RABIT (WebDAV = folder UNC), rozpoznanie po prefiksie, import, kompletność."""

from __future__ import annotations

import json
import os
import sqlite3
import time
from pathlib import Path

import openpyxl
import pytest

from ahd.baza import Database
from ahd.etap1 import cli
from ahd.konfiguracja import ConfigError, SourceDef, load_project_requirements, load_sources, match_source
from ahd.zrodla.webdav import unc_from_url

HEADER = "Obiekt;Element PSP;Wartość/WK;Data księgowania\n"

SOURCES_CSV = """# źródła testowe
Prefiks;KodZrodla;Opis
ACTUALS_PAF;ACTUALS_PAF;Koszty PAF
FORECAST_PAF;FORECAST_PAF;Prognoza PAF
ETC_PAF;ETC_PAF;ETC PAF
ACTUALS_CES;ACTUALS_CES;Koszty CES
"""

PROJECTS_CSV = """Projekt;KodZrodla
PAF-001;ACTUALS_PAF
PAF-001;FORECAST_PAF
PAF-001;ETC_PAF
ABC-002;ACTUALS_PAF
"""


def _xlsx(path: Path, rows: int, header=("Element PSP", "Kwota", "Data")) -> None:
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.append(list(header))
    for i in range(rows):
        ws.append([f"F16-CAS-01.1.{i:02d}", 100 + i, "2026-09-15"])
    wb.save(path)


@pytest.fixture()
def konf(tmp_path):
    d = tmp_path / "konfiguracja"
    d.mkdir()
    (d / "zrodla_rabit.csv").write_text("\ufeff" + SOURCES_CSV, encoding="utf-8")
    (d / "projekty_zrodla.csv").write_text(PROJECTS_CSV, encoding="utf-8")
    return d


@pytest.fixture()
def rabit(tmp_path):
    """Folder udający bibliotekę RABIT widzianą przez WebDAV."""
    src = tmp_path / "rabit"
    src.mkdir()
    (src / "ACTUALS_PAF_01.csv").write_bytes((HEADER + "".join(f"KO;F16-CAS-01.1.{i};{i},50;2026-09-0{i % 9 + 1}\n" for i in range(5))).encode("cp1250"))
    (src / "ACTUALS_PAF_02.csv").write_bytes((HEADER + "".join(f"KO;F16-CAS-02.1.{i};{i},00;2026-09-10\n" for i in range(3))).encode("cp1250"))
    _xlsx(src / "FORECAST_PAF.xlsx", 4)
    _xlsx(src / "Workaround PAF2.xlsx", 2)
    (src / "~$FORECAST_PAF.xlsx").write_bytes(b"blokada Excela")
    return src


def _db(tmp_path):
    return Database("sqlite:///" + (tmp_path / "LZ" / "ahd_mvp.sqlite").as_posix())


def _import(src, tmp_path, konf, *extra):
    return cli.main(["import", "--folder", str(src), "--landing", str(tmp_path / "LZ"), "--konfiguracja", str(konf), *extra])


def _last_decisions(db):
    return dict(db.conn.execute(
        "SELECT NazwaPliku, Decyzja FROM meta_SourceFileSeen WHERE BatchId="
        "(SELECT BatchId FROM meta_ImportBatch ORDER BY Start DESC, rowid DESC LIMIT 1)").fetchall())


# --- konfiguracja --------------------------------------------------------------

def test_prefix_match_longest_wins_and_case_insensitive():
    sources = [SourceDef("ACTUALS", "ACT"), SourceDef("ACTUALS_PAF", "ACTUALS_PAF")]
    assert match_source("ACTUALS_PAF_01.xlsx", sources).code == "ACTUALS_PAF"
    assert match_source("actuals_ces.xlsx", sources).code == "ACT"
    assert match_source("Workaround PAF2.xlsx", sources) is None


def test_config_validation(konf):
    sources = load_sources(konf)
    assert load_project_requirements(konf, sources)["PAF-001"] == ["ACTUALS_PAF", "FORECAST_PAF", "ETC_PAF"]
    (konf / "zrodla_rabit.csv").write_text(SOURCES_CSV + "actuals_paf;INNE;dubel\n", encoding="utf-8")
    with pytest.raises(ConfigError, match="zdefiniowany już"):
        load_sources(konf)
    (konf / "zrodla_rabit.csv").write_text(SOURCES_CSV, encoding="utf-8")
    (konf / "projekty_zrodla.csv").write_text(PROJECTS_CSV + "XYZ;NIEZNANE\n", encoding="utf-8")
    with pytest.raises(ConfigError, match="nie jest zdefiniowane"):
        load_project_requirements(konf, load_sources(konf))


def test_missing_config_is_reported(rabit, tmp_path, capsys):
    assert _import(rabit, tmp_path, tmp_path / "brak") == 1
    assert "Brak pliku konfiguracji" in capsys.readouterr().err


# --- WebDAV --------------------------------------------------------------------

def test_webdav_path_from_link():
    expected = "\\\\lmsp4-intl.external.lmco.com@SSL\\DavWWWRoot\\sites\\RabbitReporting\\Shared Documents\\E456659"
    assert unc_from_url("https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659") == expected
    view = ("https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/Forms/AllItems.aspx"
            "?e=5%3A1&RootFolder=%2fsites%2fRabbitReporting%2fShared%20Documents%2fE456659&FolderCTID=0x01")
    assert unc_from_url(view) == expected
    assert unc_from_url("\\\\host@SSL\\DavWWWRoot\\x") == "\\\\host@SSL\\DavWWWRoot\\x"
    with pytest.raises(ValueError):
        unc_from_url("C:\\dane")


# --- pobierz -------------------------------------------------------------------

def test_pobierz_copies_only_new_and_changed(rabit, tmp_path, capsys):
    cel = tmp_path / "Do_importu"
    args = ["pobierz", "--folder", str(rabit), "--cel", str(cel)]

    assert cli.main([*args, "--dry-run"]) == 0 and not cel.exists()
    assert cli.main(args) == 0
    assert sorted(p.name for p in cel.iterdir()) == ["ACTUALS_PAF_01.csv", "ACTUALS_PAF_02.csv", "FORECAST_PAF.xlsx", "Workaround PAF2.xlsx"]
    capsys.readouterr()

    assert cli.main(args) == 0
    assert "skopiowany 0, bez zmian 4" in capsys.readouterr().out

    # RABIT nadpisał plik tą samą nazwą.
    changed = rabit / "ACTUALS_PAF_02.csv"
    changed.write_bytes(changed.read_bytes() + "KO;x;1,00;2026-09-20\n".encode("cp1250"))
    os.utime(changed, (time.time() + 60, time.time() + 60))
    assert cli.main(args) == 0
    assert "skopiowany 1, bez zmian 3" in capsys.readouterr().out
    assert (cel / "ACTUALS_PAF_02.csv").read_bytes() == changed.read_bytes()


# --- import --------------------------------------------------------------------

def test_import_recognized_files_by_prefix(rabit, tmp_path, konf, capsys):
    assert _import(rabit, tmp_path, konf) == 0
    assert "nierozpoznane 1" in capsys.readouterr().out
    db = _db(tmp_path)
    files = {row[2]: row for row in db.history(50)}
    assert set(files) == {"ACTUALS_PAF_01.csv", "ACTUALS_PAF_02.csv", "FORECAST_PAF.xlsx"}
    _when, _user, _name, code, rows, sha = files["ACTUALS_PAF_01.csv"]
    assert (code, rows) == ("ACTUALS_PAF", 5) and db.count_rows(sha) == 5
    assert files["ACTUALS_PAF_02.csv"][3] == "ACTUALS_PAF"
    assert files["FORECAST_PAF.xlsx"][3:5] == ("FORECAST_PAF", 4)
    assert _last_decisions(db)["Workaround PAF2.xlsx"] == "nierozpoznany"
    row = db.conn.execute("SELECT Dane FROM stg_RawRow WHERE Sha256=? AND NrWiersza=1", (sha,)).fetchone()
    assert json.loads(row[0]) == ["KO", "F16-CAS-01.1.0", "0,50", "2026-09-01"]
    cols, enc = db.conn.execute("SELECT Kolumny, Kodowanie FROM meta_SourceFile WHERE Sha256=?", (sha,)).fetchone()
    assert json.loads(cols)[2] == "Wartość/WK" and enc == "cp1250"
    landing = [p for p in (tmp_path / "LZ").rglob("*") if p.is_file() and p.suffix != ".sqlite"]
    assert len(landing) == 3 and not (tmp_path / "LZ" / "_tmp").exists()
    db.close()


def test_files_of_one_source_with_different_columns_are_all_imported(rabit, tmp_path, konf):
    (rabit / "ACTUALS_PAF_02.csv").write_bytes("Obiekt;Inna kolumna\nKO;1\n".encode("cp1250"))
    assert _import(rabit, tmp_path, konf, "--filtr", "ACTUALS*") == 0
    decisions = _last_decisions(_db(tmp_path))
    assert decisions == {"ACTUALS_PAF_01.csv": "zaimportowany", "ACTUALS_PAF_02.csv": "zaimportowany"}


def test_unrecognized_file_is_imported_after_prefix_is_added(rabit, tmp_path, konf):
    assert _import(rabit, tmp_path, konf) == 0
    with (konf / "zrodla_rabit.csv").open("a", encoding="utf-8") as fh:
        fh.write("Workaround;WORKAROUND;Obejścia\n")
    assert _import(rabit, tmp_path, konf) == 0
    decisions = _last_decisions(_db(tmp_path))
    assert decisions["Workaround PAF2.xlsx"] == "zaimportowany"
    assert decisions["ACTUALS_PAF_01.csv"] == "pominiety (metadane)"


def test_second_import_skips_without_copying(rabit, tmp_path, konf, monkeypatch):
    assert _import(rabit, tmp_path, konf) == 0
    from ahd.zrodla.folder import FolderSource

    copies = []
    original = FolderSource.download
    monkeypatch.setattr(FolderSource, "download", lambda self, f, d: copies.append(f.name) or original(self, f, d))
    assert _import(rabit, tmp_path, konf) == 0
    assert copies == []
    (_start, _user, seen, imp, skip, err, status), *_ = list(_db(tmp_path).batches(1))
    assert (seen, imp, skip, err, status) == (4, 0, 4, 0, "zakonczony")


def test_overwritten_file_and_duplicate_by_hash(rabit, tmp_path, konf):
    assert _import(rabit, tmp_path, konf) == 0
    changed = rabit / "ACTUALS_PAF_02.csv"
    changed.write_bytes(changed.read_bytes() + "KO;F16-CAS-02.1.9;1,00;2026-09-20\n".encode("cp1250"))
    (rabit / "FORECAST_PAF_kopia.xlsx").write_bytes((rabit / "FORECAST_PAF.xlsx").read_bytes())
    assert _import(rabit, tmp_path, konf) == 0
    db = _db(tmp_path)
    assert _last_decisions(db) == {
        "ACTUALS_PAF_01.csv": "pominiety (metadane)", "ACTUALS_PAF_02.csv": "zaimportowany",
        "FORECAST_PAF.xlsx": "pominiety (metadane)", "FORECAST_PAF_kopia.xlsx": "duplikat",
        "Workaround PAF2.xlsx": "nierozpoznany",
    }
    # Obie wersje ACTUALS_PAF_02 są w historii (archiwalność).
    assert db.conn.execute("SELECT COUNT(*) FROM meta_SourceFile WHERE NazwaPliku='ACTUALS_PAF_02.csv'").fetchone()[0] == 2


def test_full_check_finds_duplicates(rabit, tmp_path, konf):
    assert _import(rabit, tmp_path, konf) == 0
    assert _import(rabit, tmp_path, konf, "--pelne-sprawdzenie") == 0
    (_start, _user, _seen, imp, skip, _err, _status), *_ = list(_db(tmp_path).batches(1))
    assert (imp, skip) == (0, 4)


def test_broken_file_does_not_stop_import(rabit, tmp_path, konf):
    (rabit / "ETC_PAF.xlsx").write_bytes(b"to nie jest excel")
    assert _import(rabit, tmp_path, konf) == 1
    db = _db(tmp_path)
    assert db.conn.execute("SELECT COUNT(*) FROM meta_SourceFile").fetchone()[0] == 3
    assert _last_decisions(db)["ETC_PAF.xlsx"] == "blad"


def test_dry_run_history_and_single_file(rabit, tmp_path, konf, capsys):
    lz = str(tmp_path / "LZ")
    assert _import(rabit, tmp_path, konf, "--dry-run") == 0
    out = capsys.readouterr().out
    assert "Plików      : 4" in out and "nierozpoznany" in out
    assert not (tmp_path / "LZ").exists()
    assert cli.main(["import", "--folder", str(rabit / "FORECAST_PAF.xlsx"), "--landing", lz, "--konfiguracja", str(konf)]) == 0
    assert cli.main(["historia", "--landing", lz]) == 0
    assert "FORECAST_PAF.xlsx" in capsys.readouterr().out


def test_lista_shows_recognized_source(rabit, konf, capsys):
    assert cli.main(["lista", "--folder", str(rabit), "--konfiguracja", str(konf)]) == 0
    out = capsys.readouterr().out
    assert "ACTUALS_PAF" in out and "nierozpoznanych: 1" in out


# --- kompletność ---------------------------------------------------------------

def test_kompletnosc_per_project(rabit, tmp_path, konf, capsys):
    lz = str(tmp_path / "LZ")
    assert _import(rabit, tmp_path, konf) == 0
    capsys.readouterr()
    assert cli.main(["kompletnosc", "--landing", lz, "--konfiguracja", str(konf)]) == 1
    out = capsys.readouterr().out
    assert "PAF-001: NIEKOMPLETNY" in out and "✗ ETC_PAF" in out and "✓ ACTUALS_PAF" in out
    assert "ABC-002: komplet" in out and "niekompletnych: 1" in out

    _xlsx(rabit / "ETC_PAF.xlsx", 1)
    assert _import(rabit, tmp_path, konf) == 0
    assert cli.main(["kompletnosc", "--landing", lz, "--konfiguracja", str(konf)]) == 0
    assert cli.main(["kompletnosc", "--landing", lz, "--konfiguracja", str(konf), "--maks-wiek-dni", "-1"]) == 1


# --- baza ----------------------------------------------------------------------

def test_old_sqlite_database_is_migrated(tmp_path):
    path = tmp_path / "LZ" / "ahd_mvp.sqlite"
    path.parent.mkdir()
    conn = sqlite3.connect(path)
    conn.execute("CREATE TABLE meta_SourceFile (Sha256 TEXT PRIMARY KEY, TypRaportu TEXT, Zaimportowano TEXT)")
    conn.execute("INSERT INTO meta_SourceFile VALUES ('abc', 'CJI3', '2026-09-29T10:00:00')")
    conn.commit()
    conn.close()
    db = _db(tmp_path)
    assert db.conn.execute("SELECT KodZrodla FROM meta_SourceFile").fetchone()[0] == "CJI3"
    db.close()
