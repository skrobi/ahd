"""Testy parserów i metody owssvr w narzędziu narzedzia/test_dostepu_rabit.py (bez Windows)."""

from __future__ import annotations

import importlib.util
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import pytest

spec = importlib.util.spec_from_file_location("tdr", Path(__file__).parents[1] / "narzedzia" / "test_dostepu_rabit.py")
tdr = importlib.util.module_from_spec(spec)
sys.modules["tdr"] = tdr  # wymagane przez @dataclass
spec.loader.exec_module(tdr)

OWSSVR_XML = """<xml xmlns:s='uuid:BDC6E3F0-6DA3-11d1-A2A3-00AA00C14882' xmlns:dt='uuid:C2F41010-65B3-11d1-A29F-00AA00C14882'
 xmlns:rs='urn:schemas-microsoft-com:rowset' xmlns:z='#RowsetSchema'>
<rs:data ItemCount="3">
<z:row ows_FileLeafRef='1;#CJI3_F16_cz1.xlsx' ows_FSObjType='1;#0' ows_FileRef='1;#sites/RabbitReporting/Shared Documents/E456659/CJI3_F16_cz1.xlsx'
  ows_File_x0020_Size='1;#123456' ows_Modified='2026-09-28 05:12:00' />
<z:row ows_FileLeafRef='2;#Archiwum' ows_FSObjType='2;#1' ows_FileRef='2;#sites/RabbitReporting/Shared Documents/E456659/Archiwum' />
<z:row ows_LinkFilename='ZRD_KKAJ.xlsx' ows_FSObjType='3;#0' ows_EncodedAbsUrl='https://host/sites/RabbitReporting/Shared%20Documents/E456659/ZRD_KKAJ.xlsx'
  ows_File_x0020_Size='3;#999' ows_Modified='2026-09-28 05:13:00' />
</rs:data></xml>"""


def test_default_list_definition():
    ld = tdr.parse_list_xml(tdr.DEFAULT_LIST_XML)
    assert ld.web == "https://lmsp4-intl.external.lmco.com/sites/RabbitReporting"
    assert ld.list_id == "{6BBBA130-435C-4893-93A3-12FDD034BDC2}"
    assert ld.root_folder == "/sites/RabbitReporting/Shared Documents/E456659"
    url = tdr.owssvr_url(ld)
    assert url.startswith("https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/_vti_bin/owssvr.dll?XMLDATA=1&List=")
    assert "RootFolder=/sites/RabbitReporting/Shared%20Documents/E456659" in url


def test_parse_iqy():
    iqy = ("WEB\n1\nhttps://host/sites/RabbitReporting/_vti_bin/owssvr.dll?XMLDATA=1&List={AAA}&View={BBB}"
           "&RowLimit=0&RootFolder=%2fsites%2fRabbitReporting%2fShared%20Documents%2fE456659\n\nSelection={AAA}-{BBB}\n")
    ld = tdr.parse_iqy(iqy)
    assert (ld.web, ld.list_id, ld.view_id) == ("https://host/sites/RabbitReporting", "{AAA}", "{BBB}")
    assert ld.root_folder == "/sites/RabbitReporting/Shared Documents/E456659"
    assert tdr.parse_list_xml(ld.xml).list_id == "{AAA}"


def test_parse_owssvr_rows_skips_folders():
    ld = tdr.parse_list_xml(tdr.DEFAULT_LIST_XML)
    rows = tdr.parse_owssvr_rows(OWSSVR_XML, ld)
    assert [r["nazwa"] for r in rows] == ["CJI3_F16_cz1.xlsx", "ZRD_KKAJ.xlsx"]
    assert rows[0]["url"].endswith("/sites/RabbitReporting/Shared%20Documents/E456659/CJI3_F16_cz1.xlsx")
    assert rows[0]["rozmiar"] == "123456"


@pytest.fixture()
def server():
    state = {"mode": "ok"}

    class H(BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def do_GET(self):
            if state["mode"] == "denied":
                body = b"<html><head><title>Access Denied</title></head></html>"
                self.send_response(403)
                self.send_header("Content-Type", "text/html")
            elif "owssvr.dll" in self.path:
                body = OWSSVR_XML.encode()
                self.send_response(200)
                self.send_header("Content-Type", "text/xml; charset=utf-8")
            else:
                body = b"PK\x03\x04 dane pliku"
                self.send_response(200)
                self.send_header("Content-Type", "application/octet-stream")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

    srv = ThreadingHTTPServer(("127.0.0.1", 0), H)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    yield f"http://127.0.0.1:{srv.server_address[1]}", state
    srv.shutdown()


def test_owssvr_and_download(server, tmp_path, capsys):
    base, state = server
    xml = tdr.DEFAULT_LIST_XML.replace("https://lmsp4-intl.external.lmco.com", base)
    ld = tdr.parse_list_xml(xml)
    res = tdr.test_owssvr(ld)
    assert res.status == "DZIALA" and len(res.pliki) == 2
    dl = tdr.test_download(base + "/sites/RabbitReporting/Shared%20Documents/E456659/CJI3_F16_cz1.xlsx")
    assert dl.status == "DZIALA"

    state["mode"] = "denied"
    res = tdr.test_owssvr(ld)
    assert res.status == "NIE DZIALA" and any("Access Denied" in s for s in res.szczegoly)

    f = tmp_path / "lista.xml"
    f.write_text(xml, encoding="utf-8")
    assert tdr.main(["--lista-xml", str(f), "--raport", str(tmp_path / "r.json")]) == 0
    out = capsys.readouterr().out
    assert "żadna metoda automatyczna nie działa" in out and (tmp_path / "r.json").exists()
