"""Testy MVP etapu 1 na symulowanym serwerze SharePoint (REST API, odata=verbose)."""

from __future__ import annotations

import json
import re
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote

import openpyxl
import pytest

from ahd.etap1 import cli, inspekcja, landing
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
    state = {"require_auth": False}

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

        def do_GET(self):
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


def test_pobierz_end_to_end(sharepoint, tmp_path, capsys):
    cel = tmp_path / "LZ"
    args = ["pobierz", "--url", sharepoint["url"], "--auth", "none", "--cel", str(cel), "--zakres", "F16",
            "--okres", "2026-09", "--tydzien", "40", "--filtr", "*.csv", "--filtr", "*.xlsx"]
    assert cli.main(args) == 0
    run = cel / "F16" / "2026-09" / "R-F16-2026-09-T40"
    manifest = json.loads((run / "manifest.json").read_text(encoding="utf-8"))
    by_name = {r["plik"]: r for r in manifest["pliki"]}
    assert set(by_name) == {"CJI3_F16_cz1.csv", "CJI3_F16_cz2.csv", "ZRD_KKAJ_F16.xlsx"}
    assert all(r["status"] == "nowy" for r in by_name.values())
    assert by_name["CJI3_F16_cz1.csv"]["sha256"] == landing.sha256_file(run / "CJI3_F16_cz1.csv")
    assert by_name["CJI3_F16_cz1.csv"]["inspekcja"]["wiersze"] == 5
    assert by_name["CJI3_F16_cz1.csv"]["inspekcja"]["kodowanie"] == "cp1250"
    assert by_name["CJI3_F16_cz1.csv"]["inspekcja"]["kolumny"][2] == "Wartość/WK"
    assert by_name["ZRD_KKAJ_F16.xlsx"]["inspekcja"]["wiersze"] == 4
    (zestaw,) = manifest["zestawy"]
    assert zestaw["zestaw"] == "CJI3_F16" and zestaw["waga"] == "OK" and zestaw["wiersze_razem"] == 8
    assert not list(run.glob("*.part"))

    # Ten sam przebieg drugi raz – odmowa bez --nadpisz.
    assert cli.main(args) == 2

    # Kolejny tydzień: te same pliki są rozpoznane jako bez zmian, zmieniony plik jako zmieniony.
    sharepoint["store"][FOLDER]["files"]["CJI3_F16_cz2.csv"] += "KO;F16-CAS-02.1.9;1,00;2026-09-20\n".encode("cp1250")
    args2 = [a if a != "40" else "41" for a in args]
    assert cli.main(args2) == 0
    m2 = json.loads((cel / "F16" / "2026-09" / "R-F16-2026-09-T41" / "manifest.json").read_text(encoding="utf-8"))
    statuses = {r["plik"]: r["status"] for r in m2["pliki"]}
    assert statuses == {"CJI3_F16_cz1.csv": "bez zmian", "CJI3_F16_cz2.csv": "zmieniony", "ZRD_KKAJ_F16.xlsx": "bez zmian"}


def test_inconsistent_parts_fail(sharepoint, tmp_path):
    sharepoint["store"][FOLDER]["files"]["CJI3_F16_cz2.csv"] = "Obiekt;Inna kolumna\nKO;1\n".encode("cp1250")
    code = cli.main(["pobierz", "--url", sharepoint["url"], "--auth", "none", "--cel", str(tmp_path / "LZ"),
                     "--zakres", "F16", "--okres", "2026-09", "--tydzien", "40", "--filtr", "CJI3*"])
    assert code == 1
    manifest = json.loads(next((tmp_path / "LZ").rglob("manifest.json")).read_text(encoding="utf-8"))
    assert manifest["zestawy"][0]["waga"] == "BLAD"


def test_folder_source_and_dry_run(tmp_path, capsys):
    src = tmp_path / "zrodlo"
    src.mkdir()
    (src / "CJI3_S70i.csv").write_text("a;b\n1;2\n3;4\n", encoding="utf-8")
    (src / "~$CJI3_S70i.csv").write_text("blokada", encoding="utf-8")
    assert cli.main(["pobierz", "--folder", str(src), "--cel", str(tmp_path / "LZ"), "--zakres", "S70i", "--zamkniecie",
                     "--okres", "2026-08", "--dry-run"]) == 0
    assert "Plików   : 1" in capsys.readouterr().out
    assert not (tmp_path / "LZ").exists()
    assert cli.main(["pobierz", "--folder", str(src), "--cel", str(tmp_path / "LZ"), "--zakres", "S70i", "--zamkniecie",
                     "--okres", "2026-08"]) == 0
    run = tmp_path / "LZ" / "S70i" / "2026-08" / "R-S70i-2026-08-Z"
    rec = json.loads((run / "manifest.json").read_text(encoding="utf-8"))["pliki"][0]
    assert rec["inspekcja"]["wiersze"] == 2 and rec["status"] == "nowy"


@pytest.mark.parametrize(
    "name,expected",
    [("CJI3_F16_cz1.csv", "CJI3_F16"), ("CJI3 F16 part 3.txt", "CJI3 F16"), ("Raport (2).xlsx", "Raport"),
     ("E456659.xlsx", "E456659"), ("export_top10.csv", "export_top10")],
)
def test_set_name(name, expected):
    assert inspekcja.set_name(name) == expected
