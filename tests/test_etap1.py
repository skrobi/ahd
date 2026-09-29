"""Testy MVP etapu 1 na symulowanym serwerze SharePoint (REST API, odata=verbose)."""

from __future__ import annotations

import json
import re
import threading
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote

import openpyxl
import pytest

from ahd.baza import Database
from ahd.etap1 import cli, inspekcja
from ahd.zrodla.sharepoint import SharePointClient, SharePointError, parse_sharepoint_url

SITE = "/sites/RabbitReporting"
FOLDER = SITE + "/Shared Documents/E456659"

LINK = (
    "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/Forms/AllItems.aspx"
    "?e=5%3A728ba8d904af4e8b8f1928b8decdb15b&RootFolder=%2fsites%2fRabbitReporting%2fShared%20Documents"
    "%2fE456659&FolderCTID=0x0120005F75424BC06B5A49BD62FB1705210E63"
)


def _xlsx_bytes(tmp_path: Path, header: list[str], rows: int) -> bytes:
    wb = openpyxl.Workbook()
    ws = wb.active
    ws.title = "Dane"
    ws.append(header)
    for i in range(rows):
        ws.append([f"F16-CAS-01.1.{i:02d}", 100 + i, "2026-09-15"])
    path = tmp_path / "tmp.xlsx"
    wb.save(path)
    return path.read_bytes()


@pytest.fixture()
def sharepoint(tmp_path):
    """Serwer udający SharePoint: {ścieżka folderu: {"files": {nazwa: bajty}, "folders": [...]}}."""
    header = "Obiekt;Element PSP;Wartość/WK;Data księgowania\n"
    store = {
        FOLDER: {
            "files": {
                "CJI3_F16_cz1.csv": (header + "".join(f"KO;F16-CAS-01.1.{i};{i},50;2026-09-0{i % 9 + 1}\n" for i in range(5))).encode("cp1250"),
                "CJI3_F16_cz2.csv": (header + "".join(f"KO;F16-CAS-02.1.{i};{i},00;2026-09-10\n" for i in range(3))).encode("cp1250"),
                "ZRD_KKAJ_F16.xlsx": _xlsx_bytes(tmp_path, ["Element PSP", "Kwota", "Data"], 4),
                "Notatka's.txt": b"linia 1\nlinia 2\n",
            },
            "folders": [FOLDER + "/Archiwum", FOLDER + "/Forms"],
        },
        FOLDER + "/Archiwum": {"files": {"stary.csv": b"a;b\n1;2\n"}, "folders": []},
    }
    state = {"require_auth": False, "mode": None}

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *a):  # cisza w testach
            pass

        def _json(self, obj, code=200):
            body = json.dumps(obj).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json;odata=verbose")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def _html(self, title, code=200):
            body = f"<html><head><title>{title}</title></head><body>form</body></html>".encode()
            self.send_response(code)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            if self.path.startswith("/adfs/ls/"):
                return self._html("Sign In")
            if state["mode"] == "login_redirect":
                self.send_response(302)
                self.send_header("Location", "/adfs/ls/?wa=wsignin1.0&wctx=SEKRETNY_TOKEN")
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            if state["mode"] == "html":
                return self._html("Strona informacyjna")
            if state["require_auth"]:
                self.send_response(401)
                self.send_header("WWW-Authenticate", "Negotiate")
                self.send_header("WWW-Authenticate", "NTLM")
                self.end_headers()
                return
            path = unquote(self.path)
            if path == SITE + "/_api/web/currentuser":
                return self._json({"d": {"LoginName": "i:0#.w|PZL\\test", "Title": "Test"}})
            m = re.match(re.escape(SITE) + r"/_api/web/GetFolderByServerRelativeUrl\('(.*)'\)\?\$expand=Files,Folders$", path)
            if m:
                folder = m.group(1).replace("''", "'")
                if folder not in store:
                    return self._json({"error": "not found"}, 404)
                node = store[folder]
                files = [
                    {"Name": n, "ServerRelativeUrl": f"{folder}/{n}", "Length": str(len(b)), "TimeLastModified": "2026-09-28T05:12:00Z"}
                    for n, b in node["files"].items()
                ]
                folders = [{"Name": f.rsplit("/", 1)[1], "ServerRelativeUrl": f} for f in node["folders"]]
                return self._json({"d": {"Files": {"results": files}, "Folders": {"results": folders}}})
            m = re.match(re.escape(SITE) + r"/_api/web/GetFileByServerRelativeUrl\('(.*)'\)/\$value$", path)
            if m:
                full = m.group(1).replace("''", "'")
                folder, name = full.rsplit("/", 1)
                data = store.get(folder, {}).get("files", {}).get(name)
                if data is None:
                    return self._json({"error": "not found"}, 404)
                self.send_response(200)
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)
                return
            self._json({"error": "bad request"}, 400)

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    host = f"http://127.0.0.1:{server.server_address[1]}"
    url = host + SITE + "/Shared%20Documents/Forms/AllItems.aspx?RootFolder=" + FOLDER.replace(" ", "%20")
    yield {"url": url, "store": store, "state": state}
    server.shutdown()


def test_parse_link_from_browser():
    loc = parse_sharepoint_url(LINK)
    assert loc.site_url == "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting"
    assert loc.folder == "/sites/RabbitReporting/Shared Documents/E456659"


def test_parse_other_link_forms():
    loc = parse_sharepoint_url("https://host/sites/X/Shared%20Documents/Forms/AllItems.aspx?id=%2Fsites%2FX%2FShared%20Documents%2FA")
    assert (loc.site_url, loc.folder) == ("https://host/sites/X", "/sites/X/Shared Documents/A")
    loc = parse_sharepoint_url("https://host/sites/X/Shared%20Documents/Forms/AllItems.aspx")
    assert loc.folder == "/sites/X/Shared Documents"
    with pytest.raises(ValueError):
        parse_sharepoint_url("C:\\dane")


def test_login_redirect_is_diagnosed(sharepoint):
    sharepoint["state"]["mode"] = "login_redirect"
    loc = parse_sharepoint_url(sharepoint["url"])
    with pytest.raises(SharePointError) as err:
        SharePointClient(loc.site_url).check()
    msg = str(err.value)
    assert "HTTP 302" in msg and "/adfs/ls/" in msg and "Tytuł strony: Sign In" in msg
    assert "ADFS" in msg and "--folder" in msg
    assert "SEKRETNY_TOKEN" not in msg  # parametry zapytania nie trafiają do komunikatu


def test_html_instead_of_json_is_diagnosed(sharepoint, capsys):
    sharepoint["state"]["mode"] = "html"
    assert cli.main(["sprawdz", "--url", sharepoint["url"], "--auth", "none"]) == 1
    err = capsys.readouterr().err
    assert "nie zwrócił danych w formacie JSON" in err and "Strona informacyjna" in err


def test_list_and_walk(sharepoint):
    loc = parse_sharepoint_url(sharepoint["url"])
    client = SharePointClient(loc.site_url)
    files, folders = client.list_folder(loc.folder)
    assert {f.name for f in files} == {"CJI3_F16_cz1.csv", "CJI3_F16_cz2.csv", "ZRD_KKAJ_F16.xlsx", "Notatka's.txt"}
    assert folders == [FOLDER + "/Archiwum"]  # folder systemowy Forms pominięty
    assert len(list(client.walk(loc.folder, recursive=True))) == 5


def test_auth_error_is_readable(sharepoint):
    sharepoint["state"]["require_auth"] = True
    loc = parse_sharepoint_url(sharepoint["url"])
    with pytest.raises(SharePointError, match="HTTP 401.*Negotiate"):
        SharePointClient(loc.site_url).check()


def _db(tmp_path):
    return Database("sqlite:///" + (tmp_path / "LZ" / "ahd_mvp.sqlite").as_posix())


def _import(sharepoint, tmp_path, *extra):
    return cli.main(["import", "--url", sharepoint["url"], "--auth", "none", "--landing", str(tmp_path / "LZ"), *extra])


def test_import_all_files_without_scope(sharepoint, tmp_path):
    assert _import(sharepoint, tmp_path) == 0
    db = _db(tmp_path)
    files = {row[2]: row for row in db.history(50)}
    assert set(files) == {"CJI3_F16_cz1.csv", "CJI3_F16_cz2.csv", "ZRD_KKAJ_F16.xlsx", "Notatka's.txt"}
    when, user, name, rtype, rows, sha = files["CJI3_F16_cz1.csv"]
    assert (rtype, rows) == ("CJI3", 5) and db.count_rows(sha) == 5
    assert files["ZRD_KKAJ_F16.xlsx"][3:5] == ("ZRD_KKAJ", 4)
    cur = db.conn.execute("SELECT Dane FROM stg_RawRow WHERE Sha256=? AND NrWiersza=1", (sha,))
    assert json.loads(cur.fetchone()[0]) == ["KO", "F16-CAS-01.1.0", "0,50", "2026-09-01"]
    cols = db.conn.execute("SELECT Kolumny, Kodowanie FROM meta_SourceFile WHERE Sha256=?", (sha,)).fetchone()
    assert json.loads(cols[0])[2] == "Wartość/WK" and cols[1] == "cp1250"
    landing_files = [p for p in (tmp_path / "LZ").rglob("*") if p.is_file() and p.suffix != ".sqlite"]
    assert len(landing_files) == 4 and not (tmp_path / "LZ" / "_tmp").exists() or not any((tmp_path / "LZ" / "_tmp").rglob("*.*"))
    db.close()


def test_second_import_skips_without_download(sharepoint, tmp_path, monkeypatch):
    assert _import(sharepoint, tmp_path) == 0
    downloads = []
    original = SharePointClient.download
    monkeypatch.setattr(SharePointClient, "download", lambda self, f, d: downloads.append(f.name) or original(self, f, d))
    assert _import(sharepoint, tmp_path) == 0
    assert downloads == []
    (start, user, seen, imp, skip, err, status), *_ = list(_db(tmp_path).batches(1))
    assert (seen, imp, skip, err, status) == (4, 0, 4, 0, "zakonczony")


def test_changed_file_and_duplicate_under_new_name(sharepoint, tmp_path):
    assert _import(sharepoint, tmp_path) == 0
    files = sharepoint["store"][FOLDER]["files"]
    files["CJI3_F16_cz2.csv"] += "KO;F16-CAS-02.1.9;1,00;2026-09-20\n".encode("cp1250")
    files["Kopia ZRD.xlsx"] = files["ZRD_KKAJ_F16.xlsx"]
    assert _import(sharepoint, tmp_path) == 0
    db = _db(tmp_path)
    decisions = dict(db.conn.execute(
        "SELECT NazwaPliku, Decyzja FROM meta_SourceFileSeen WHERE BatchId=(SELECT BatchId FROM meta_ImportBatch ORDER BY Start DESC, rowid DESC LIMIT 1)"
    ).fetchall())
    assert decisions["CJI3_F16_cz2.csv"] == "zaimportowany"
    assert decisions["Kopia ZRD.xlsx"] == "duplikat"
    assert decisions["CJI3_F16_cz1.csv"] == "pominiety (metadane)"
    assert db.conn.execute("SELECT COUNT(*) FROM meta_SourceFile").fetchone()[0] == 5


def test_full_check_downloads_and_finds_duplicates(sharepoint, tmp_path):
    assert _import(sharepoint, tmp_path) == 0
    assert _import(sharepoint, tmp_path, "--pelne-sprawdzenie") == 0
    (start, user, seen, imp, skip, err, status), *_ = list(_db(tmp_path).batches(1))
    assert (imp, skip) == (0, 4)


def test_broken_file_does_not_stop_import(sharepoint, tmp_path):
    sharepoint["store"][FOLDER]["files"]["Uszkodzony.xlsx"] = b"to nie jest excel"
    assert _import(sharepoint, tmp_path) == 1
    db = _db(tmp_path)
    assert db.conn.execute("SELECT COUNT(*) FROM meta_SourceFile").fetchone()[0] == 4
    assert db.conn.execute("SELECT Decyzja FROM meta_SourceFileSeen WHERE NazwaPliku='Uszkodzony.xlsx'").fetchone()[0] == "blad"


def test_inconsistent_parts_fail(sharepoint, tmp_path):
    sharepoint["store"][FOLDER]["files"]["CJI3_F16_cz2.csv"] = "Obiekt;Inna kolumna\nKO;1\n".encode("cp1250")
    assert _import(sharepoint, tmp_path, "--filtr", "CJI3*") == 1


def test_folder_source_dry_run_and_history(tmp_path, capsys):
    src = tmp_path / "zrodlo"
    src.mkdir()
    (src / "CJI3_S70i.csv").write_text("a;b\n1;2\n3;4\n", encoding="utf-8")
    (src / "~$CJI3_S70i.csv").write_text("blokada", encoding="utf-8")
    lz = str(tmp_path / "LZ")
    assert cli.main(["import", "--folder", str(src), "--landing", lz, "--dry-run"]) == 0
    assert "Plików      : 1" in capsys.readouterr().out
    assert cli.main(["import", "--folder", str(src), "--landing", lz]) == 0
    assert cli.main(["historia", "--landing", lz]) == 0
    out = capsys.readouterr().out
    assert "CJI3_S70i.csv" in out and "CJI3" in out


@pytest.mark.parametrize(
    "name,expected",
    [("CJI3_F16_cz1.csv", "CJI3_F16"), ("CJI3 F16 part 3.txt", "CJI3 F16"), ("Raport (2).xlsx", "Raport"),
     ("E456659.xlsx", "E456659"), ("export_top10.csv", "export_top10")],
)
def test_set_name(name, expected):
    assert inspekcja.set_name(name) == expected


def test_manual_download_zip(tmp_path, capsys):
    """Pobranie kilku plików z SharePoint w przeglądarce daje ZIP – import czyta go bez rozpakowywania."""
    inbox = tmp_path / "Do_importu"
    inbox.mkdir()
    header = "Obiekt;Element PSP;Wartość/WK\n"
    with zipfile.ZipFile(inbox / "OneDrive_1_29-09-2026.zip", "w") as zf:
        zf.writestr("E456659/CJI3_F16_cz1.csv", (header + "KO;F16-CAS-01.1.1;1,00\n").encode("cp1250"))
        zf.writestr("E456659/CJI3_F16_cz2.csv", (header + "KO;F16-CAS-02.1.1;2,00\nKO;x;3\n").encode("cp1250"))
        zf.writestr("E456659/~$CJI3_F16_cz1.csv", b"blokada")
    (inbox / "ZRD_KKAJ_F16.csv").write_text("a;b\n1;2\n", encoding="utf-8")
    lz = str(tmp_path / "LZ")

    assert cli.main(["import", "--folder", str(inbox), "--landing", lz]) == 0
    db = Database("sqlite:///" + (tmp_path / "LZ" / "ahd_mvp.sqlite").as_posix())
    names = {r[2]: r[4] for r in db.history(10)}
    assert names == {"CJI3_F16_cz1.csv": 1, "CJI3_F16_cz2.csv": 2, "ZRD_KKAJ_F16.csv": 1}

    # Tydzień później: nowy ZIP z tymi samymi plikami i jednym zmienionym.
    with zipfile.ZipFile(inbox / "OneDrive_2_06-10-2026.zip", "w") as zf:
        zf.writestr("E456659/CJI3_F16_cz1.csv", (header + "KO;F16-CAS-01.1.1;1,00\n").encode("cp1250"))
        zf.writestr("E456659/CJI3_F16_cz2.csv", (header + "KO;F16-CAS-02.1.1;2,00\nKO;x;3\nKO;y;4\n").encode("cp1250"))
    capsys.readouterr()
    assert cli.main(["import", "--folder", str(inbox), "--landing", lz]) == 0
    out = capsys.readouterr().out
    assert "zaimportowane 1, bez zmian 3, duplikaty 1" in out
    assert db.conn.execute("SELECT COUNT(*) FROM meta_SourceFile").fetchone()[0] == 4
    db.close()

    # Wskazanie bezpośrednio pliku ZIP.
    assert cli.main(["lista", "--folder", str(inbox / "OneDrive_1_29-09-2026.zip")]) == 0
    assert "Razem: 2 plików" in capsys.readouterr().out


def test_single_downloaded_file(tmp_path, capsys):
    """Ręcznie pobrany pojedynczy plik można wskazać bezpośrednio."""
    f = tmp_path / "CJI3_F16.csv"
    f.write_text("a;b\n1;2\n", encoding="utf-8")
    assert cli.main(["import", "--folder", str(f), "--landing", str(tmp_path / "LZ")]) == 0
    assert "zaimportowane 1" in capsys.readouterr().out


def test_webdav_path_from_link():
    from ahd.zrodla.webdav import unc_from_url

    assert unc_from_url("https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/Shared%20Documents/E456659") == (
        "\\\\lmsp4-intl.external.lmco.com@SSL\\DavWWWRoot\\sites\\RabbitReporting\\Shared Documents\\E456659"
    )
    assert unc_from_url(LINK).endswith("\\Shared Documents\\E456659")
    assert unc_from_url("\\\\host@SSL\\DavWWWRoot\\x") == "\\\\host@SSL\\DavWWWRoot\\x"


def test_pobierz_copies_only_new_and_changed(tmp_path, capsys):
    import os
    import time

    src, cel = tmp_path / "rabit", tmp_path / "Do_importu"
    src.mkdir()
    (src / "B6 AC1-2.xlsx").write_bytes(b"a" * 10)
    (src / "PAF2 hedge Status.xlsx").write_bytes(b"b" * 20)
    args = ["pobierz", "--folder", str(src), "--cel", str(cel)]

    assert cli.main([*args, "--dry-run"]) == 0 and not cel.exists()
    assert cli.main(args) == 0
    assert sorted(p.name for p in cel.iterdir()) == ["B6 AC1-2.xlsx", "PAF2 hedge Status.xlsx"]
    capsys.readouterr()

    assert cli.main(args) == 0
    assert "skopiowany 0, bez zmian 2" in capsys.readouterr().out

    (src / "B6 AC1-2.xlsx").write_bytes(b"c" * 11)
    os.utime(src / "B6 AC1-2.xlsx", (time.time() + 60, time.time() + 60))
    assert cli.main(args) == 0
    assert "skopiowany 1, bez zmian 1" in capsys.readouterr().out
    assert (cel / "B6 AC1-2.xlsx").read_bytes() == b"c" * 11
