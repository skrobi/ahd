"""Testy etapu 1: pobranie plików RABIT (WebDAV = folder UNC) i import do bazy."""

from __future__ import annotations

import json
import os
import time
from pathlib import Path

import openpyxl
import pytest

from ahd.baza import Database
from ahd.etap1 import cli, inspekcja
from ahd.zrodla.webdav import unc_from_url

HEADER = "Obiekt;Element PSP;Wartość/WK;Data księgowania\n"


def _xlsx(path: Path, rows: int) -> None:
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.append(["Element PSP", "Kwota", "Data"])
    for i in range(rows):
        ws.append([f"F16-CAS-01.1.{i:02d}", 100 + i, "2026-09-15"])
    wb.save(path)


@pytest.fixture()
def rabit(tmp_path):
    """Folder udający bibliotekę RABIT widzianą przez WebDAV."""
    src = tmp_path / "rabit"
    src.mkdir()
    (src / "CJI3_F16_cz1.csv").write_bytes((HEADER + "".join(f"KO;F16-CAS-01.1.{i};{i},50;2026-09-0{i % 9 + 1}\n" for i in range(5))).encode("cp1250"))
    (src / "CJI3_F16_cz2.csv").write_bytes((HEADER + "".join(f"KO;F16-CAS-02.1.{i};{i},00;2026-09-10\n" for i in range(3))).encode("cp1250"))
    _xlsx(src / "B6 AC1-2.xlsx", 4)
    (src / "~$B6 AC1-2.xlsx").write_bytes(b"blokada Excela")
    return src


def _db(tmp_path):
    return Database("sqlite:///" + (tmp_path / "LZ" / "ahd_mvp.sqlite").as_posix())


def _import(src, tmp_path, *extra):
    return cli.main(["import", "--folder", str(src), "--landing", str(tmp_path / "LZ"), *extra])


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
    assert sorted(p.name for p in cel.iterdir()) == ["B6 AC1-2.xlsx", "CJI3_F16_cz1.csv", "CJI3_F16_cz2.csv"]
    capsys.readouterr()

    assert cli.main(args) == 0
    assert "skopiowany 0, bez zmian 3" in capsys.readouterr().out

    changed = rabit / "CJI3_F16_cz2.csv"
    changed.write_bytes(changed.read_bytes() + "KO;x;1,00;2026-09-20\n".encode("cp1250"))
    os.utime(changed, (time.time() + 60, time.time() + 60))
    assert cli.main(args) == 0
    assert "skopiowany 1, bez zmian 2" in capsys.readouterr().out
    assert (cel / "CJI3_F16_cz2.csv").read_bytes() == changed.read_bytes()


# --- import --------------------------------------------------------------------

def test_import_all_files_without_scope(rabit, tmp_path):
    assert _import(rabit, tmp_path) == 0
    db = _db(tmp_path)
    files = {row[2]: row for row in db.history(50)}
    assert set(files) == {"CJI3_F16_cz1.csv", "CJI3_F16_cz2.csv", "B6 AC1-2.xlsx"}
    _when, _user, _name, rtype, rows, sha = files["CJI3_F16_cz1.csv"]
    assert (rtype, rows) == ("CJI3", 5) and db.count_rows(sha) == 5
    assert files["B6 AC1-2.xlsx"][3:5] == ("nieznany", 4)
    row = db.conn.execute("SELECT Dane FROM stg_RawRow WHERE Sha256=? AND NrWiersza=1", (sha,)).fetchone()
    assert json.loads(row[0]) == ["KO", "F16-CAS-01.1.0", "0,50", "2026-09-01"]
    cols, enc = db.conn.execute("SELECT Kolumny, Kodowanie FROM meta_SourceFile WHERE Sha256=?", (sha,)).fetchone()
    assert json.loads(cols)[2] == "Wartość/WK" and enc == "cp1250"
    landing = [p for p in (tmp_path / "LZ").rglob("*") if p.is_file() and p.suffix != ".sqlite"]
    assert len(landing) == 3 and not (tmp_path / "LZ" / "_tmp").exists()
    db.close()


def test_second_import_skips_without_copying(rabit, tmp_path, monkeypatch):
    assert _import(rabit, tmp_path) == 0
    from ahd.zrodla.folder import FolderSource

    copies = []
    original = FolderSource.download
    monkeypatch.setattr(FolderSource, "download", lambda self, f, d: copies.append(f.name) or original(self, f, d))
    assert _import(rabit, tmp_path) == 0
    assert copies == []
    (_start, _user, seen, imp, skip, err, status), *_ = list(_db(tmp_path).batches(1))
    assert (seen, imp, skip, err, status) == (3, 0, 3, 0, "zakonczony")


def test_changed_file_and_duplicate_under_new_name(rabit, tmp_path):
    assert _import(rabit, tmp_path) == 0
    changed = rabit / "CJI3_F16_cz2.csv"
    changed.write_bytes(changed.read_bytes() + "KO;F16-CAS-02.1.9;1,00;2026-09-20\n".encode("cp1250"))
    (rabit / "Kopia B6.xlsx").write_bytes((rabit / "B6 AC1-2.xlsx").read_bytes())
    assert _import(rabit, tmp_path) == 0
    db = _db(tmp_path)
    decisions = dict(db.conn.execute(
        "SELECT NazwaPliku, Decyzja FROM meta_SourceFileSeen WHERE BatchId="
        "(SELECT BatchId FROM meta_ImportBatch ORDER BY Start DESC, rowid DESC LIMIT 1)").fetchall())
    assert decisions == {"B6 AC1-2.xlsx": "pominiety (metadane)", "CJI3_F16_cz1.csv": "pominiety (metadane)",
                         "CJI3_F16_cz2.csv": "zaimportowany", "Kopia B6.xlsx": "duplikat"}
    assert db.conn.execute("SELECT COUNT(*) FROM meta_SourceFile").fetchone()[0] == 4


def test_full_check_finds_duplicates(rabit, tmp_path):
    assert _import(rabit, tmp_path) == 0
    assert _import(rabit, tmp_path, "--pelne-sprawdzenie") == 0
    (_start, _user, _seen, imp, skip, _err, _status), *_ = list(_db(tmp_path).batches(1))
    assert (imp, skip) == (0, 3)


def test_broken_file_does_not_stop_import(rabit, tmp_path):
    (rabit / "Uszkodzony.xlsx").write_bytes(b"to nie jest excel")
    assert _import(rabit, tmp_path) == 1
    db = _db(tmp_path)
    assert db.conn.execute("SELECT COUNT(*) FROM meta_SourceFile").fetchone()[0] == 3
    assert db.conn.execute("SELECT Decyzja FROM meta_SourceFileSeen WHERE NazwaPliku='Uszkodzony.xlsx'").fetchone()[0] == "blad"


def test_inconsistent_parts_fail(rabit, tmp_path):
    (rabit / "CJI3_F16_cz2.csv").write_bytes("Obiekt;Inna kolumna\nKO;1\n".encode("cp1250"))
    assert _import(rabit, tmp_path, "--filtr", "CJI3*") == 1


def test_dry_run_history_and_single_file(rabit, tmp_path, capsys):
    lz = str(tmp_path / "LZ")
    assert cli.main(["import", "--folder", str(rabit), "--landing", lz, "--dry-run"]) == 0
    assert "Plików      : 3" in capsys.readouterr().out
    assert not (tmp_path / "LZ").exists()
    # Plik pobrany ręcznie można wskazać bezpośrednio.
    assert cli.main(["import", "--folder", str(rabit / "B6 AC1-2.xlsx"), "--landing", lz]) == 0
    assert cli.main(["historia", "--landing", lz]) == 0
    assert "B6 AC1-2.xlsx" in capsys.readouterr().out


@pytest.mark.parametrize(
    "name,expected",
    [("CJI3_F16_cz1.csv", "CJI3_F16"), ("CJI3 F16 part 3.txt", "CJI3 F16"), ("Raport (2).xlsx", "Raport"),
     ("B6 AC1-2.xlsx", "B6 AC1-2"), ("export_top10.csv", "export_top10")],
)
def test_set_name(name, expected):
    assert inspekcja.set_name(name) == expected
