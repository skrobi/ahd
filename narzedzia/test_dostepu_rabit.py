"""Test dostępu do biblioteki RABIT na SharePoint – które drogi działają z tego komputera.

Uruchomienie (Windows, w katalogu repozytorium, z aktywnym .venv):

    python narzedzia\\test_dostepu_rabit.py --plik-url "<link do JEDNEGO pliku z folderu E456659>"

Opcjonalnie definicja listy z połączenia Excela (tekst polecenia <LIST>…</LIST>) albo plik .iqy:

    python narzedzia\\test_dostepu_rabit.py --lista-xml lista.xml --plik-url "<link>"
    python narzedzia\\test_dostepu_rabit.py --iqy owssvr.iqy --plik-url "<link>"

Skrypt NIE zapisuje ani nie wyświetla zawartości plików – tylko status każdej metody, liczbę
plików i nazwy (raport: raport_dostepu_rabit.json). Przed przekazaniem raportu przejrzyj nazwy plików.

Sprawdzane metody:
  A. owssvr.dll?XMLDATA=1 (to samo co „Eksport do Excela”) – lista plików przez HTTP, logowanie SSO Windows
  B. Provider OLEDB Microsoft.Office.List.OLEDB.2.0 przez ADODB (jak połączenie w Excelu)
  C. WebDAV – ścieżka \\\\host@SSL\\DavWWWRoot\\… (jak „Otwórz w Eksploratorze”)
  D. Bezpośrednie pobranie jednego pliku przez HTTP (SSO Windows)
"""

from __future__ import annotations

import argparse
import json
import os
import platform
import re
import struct
import sys
import tempfile
import threading
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from typing import Any
from urllib.parse import parse_qs, quote, unquote, urlsplit

# Definicja listy z połączenia Excela (podana w rozmowie) – domyślna, gdy nie wskazano innej.
DEFAULT_LIST_XML = (
    "<LIST><VIEWGUID>{7228D59B-433E-47AE-821D-306CA7C1058D}</VIEWGUID>"
    "<LISTNAME>{6BBBA130-435C-4893-93A3-12FDD034BDC2}</LISTNAME>"
    "<LISTWEB>https://lmsp4-intl.external.lmco.com/sites/RabbitReporting/_vti_bin</LISTWEB>"
    "<LISTSUBWEB></LISTSUBWEB>"
    "<ROOTFOLDER>/sites/RabbitReporting/Shared%20Documents/E456659</ROOTFOLDER></LIST>"
)

OLEDB_CONNECTIONS = [
    'Provider=Microsoft.Office.List.OLEDB.2.0;Data Source="";ApplicationName=Excel;Version=12.0.0.0',
    "Provider=Microsoft.Office.List.OLEDB.2.0;",
]


@dataclass
class ListDef:
    web: str  # https://host/sites/RabbitReporting (bez /_vti_bin)
    list_id: str
    view_id: str
    root_folder: str  # /sites/RabbitReporting/Shared Documents/E456659
    xml: str

    @property
    def host(self) -> str:
        return urlsplit(self.web).netloc


@dataclass
class Result:
    metoda: str
    status: str = "NIE SPRAWDZONO"  # DZIALA / NIE DZIALA / POMINIETO
    szczegoly: list[str] = field(default_factory=list)
    pliki: list[dict[str, Any]] = field(default_factory=list)


def parse_list_xml(text: str) -> ListDef:
    root = ET.fromstring(text.strip())

    def get(tag: str) -> str:
        el = root.find(tag)
        return (el.text or "").strip() if el is not None else ""

    web = get("LISTWEB")
    web = re.sub(r"/_vti_bin/?$", "", web, flags=re.IGNORECASE)
    return ListDef(web=web, list_id=get("LISTNAME"), view_id=get("VIEWGUID"),
                   root_folder=unquote(get("ROOTFOLDER")), xml=text.strip())


def parse_iqy(text: str) -> ListDef:
    """Plik .iqy z „Eksportu do Excela”: zawiera adres owssvr.dll z identyfikatorami listy i widoku."""
    url = next((line.strip() for line in text.splitlines() if line.strip().lower().startswith("http")), "")
    if not url:
        raise ValueError("W pliku .iqy nie ma adresu http(s)")
    parts = urlsplit(url)
    q = {k.lower(): v[0] for k, v in parse_qs(parts.query).items()}
    web = f"{parts.scheme}://{parts.netloc}" + re.sub(r"/_vti_bin/owssvr\.dll$", "", parts.path, flags=re.IGNORECASE)
    root_folder = unquote(q.get("rootfolder", ""))
    xml = (f"<LIST><VIEWGUID>{q.get('view', '')}</VIEWGUID><LISTNAME>{q.get('list', '')}</LISTNAME>"
           f"<LISTWEB>{web}/_vti_bin</LISTWEB><LISTSUBWEB></LISTSUBWEB>"
           f"<ROOTFOLDER>{quote(root_folder)}</ROOTFOLDER></LIST>")
    return ListDef(web=web, list_id=q.get("list", ""), view_id=q.get("view", ""), root_folder=root_folder, xml=xml)


def owssvr_url(ld: ListDef) -> str:
    return (f"{ld.web}/_vti_bin/owssvr.dll?XMLDATA=1&List={quote(ld.list_id)}&View={quote(ld.view_id)}"
            f"&RowLimit=0&RootFolder={quote(ld.root_folder)}")


def _lookup(value: str) -> str:
    # Pola SharePoint w formacie "123;#wartość".
    return value.split(";#", 1)[1] if ";#" in value else value


def parse_owssvr_rows(xml_text: str, ld: ListDef) -> list[dict[str, Any]]:
    """Wiersze z odpowiedzi owssvr.dll (<rs:data><z:row ows_…/>) – tylko pliki, bez folderów."""
    root = ET.fromstring(xml_text)
    host = f"{urlsplit(ld.web).scheme}://{ld.host}"
    files = []
    for el in root.iter():
        if not el.tag.endswith("}row") and el.tag != "row":
            continue
        a = el.attrib
        if _lookup(a.get("ows_FSObjType", "0")) == "1":
            continue
        ref = _lookup(a.get("ows_FileRef", ""))
        url = a.get("ows_EncodedAbsUrl") or (host + "/" + quote(ref.lstrip("/")) if ref else "")
        files.append({
            "nazwa": a.get("ows_LinkFilename") or a.get("ows_FileLeafRef", "").split(";#")[-1] or ref.rsplit("/", 1)[-1],
            "url": url,
            "rozmiar": _lookup(a.get("ows_File_x0020_Size", "")),
            "zmodyfikowany": a.get("ows_Modified", ""),
        })
    return files


def _session():
    import requests

    try:
        import truststore

        truststore.inject_into_ssl()
    except ImportError:
        pass
    s = requests.Session()
    try:
        from requests_negotiate_sspi import HttpNegotiateAuth

        s.auth = HttpNegotiateAuth()
    except ImportError:
        pass  # poza Windows – test i tak nie przejdzie logowania, ale pokaże odpowiedź serwera
    return s


def _describe(resp) -> list[str]:
    out = [f"HTTP {r.status_code} {urlsplit(r.url).scheme}://{urlsplit(r.url).netloc}{urlsplit(r.url).path}"
           for r in [*resp.history, resp]]
    out.append(f"Typ odpowiedzi: {resp.headers.get('Content-Type', 'brak')}")
    for h in ("WWW-Authenticate", "MicrosoftSharePointTeamServices", "X-MS-InvokeApp"):
        if h in resp.headers:
            out.append(f"{h}: {resp.headers[h]}")
    if "html" in resp.headers.get("Content-Type", "").lower():
        m = re.search(r"<title[^>]*>(.*?)</title>", resp.text[:20000], re.IGNORECASE | re.DOTALL)
        if m:
            out.append("Tytuł strony: " + " ".join(m.group(1).split())[:120])
    return out


def test_owssvr(ld: ListDef) -> Result:
    res = Result("A. owssvr.dll (Eksport do Excela) przez HTTP")
    try:
        resp = _session().get(owssvr_url(ld), timeout=120)
        res.szczegoly += _describe(resp)
        if resp.status_code == 200 and "xml" in resp.headers.get("Content-Type", "").lower():
            res.pliki = parse_owssvr_rows(resp.text, ld)
            res.status = "DZIALA"
            res.szczegoly.append(f"Plików na liście: {len(res.pliki)}")
        else:
            res.status = "NIE DZIALA"
    except Exception as exc:
        res.status = "NIE DZIALA"
        res.szczegoly.append(f"{type(exc).__name__}: {exc}")
    return res


def test_oledb(ld: ListDef) -> Result:
    res = Result("B. Provider Microsoft.Office.List.OLEDB.2.0 (ADODB)")
    bits = struct.calcsize("P") * 8
    res.szczegoly.append(f"Python {platform.python_version()} {bits}-bit – bitowość musi być taka jak pakietu Office")
    if os.name != "nt":
        res.status = "POMINIETO"
        res.szczegoly.append("Tylko Windows")
        return res
    try:
        import pythoncom
        import win32com.client
    except ImportError:
        res.status = "POMINIETO"
        res.szczegoly.append("Brak pakietu pywin32 (pip install pywin32)")
        return res

    pythoncom.CoInitialize()
    last_error = ""
    for conn_str in OLEDB_CONNECTIONS:
        conn = win32com.client.Dispatch("ADODB.Connection")
        try:
            conn.Open(conn_str)
        except Exception as exc:
            last_error = f"Otwarcie połączenia ({conn_str[:60]}…): {exc}"
            continue
        # Excel zapisuje tekst polecenia <LIST>; próbujemy typowych typów polecenia ADO.
        for option in (-1, 1, 2, 512):
            rs = win32com.client.Dispatch("ADODB.Recordset")
            try:
                rs.Open(ld.xml, conn, 0, 1, option)
            except Exception as exc:
                last_error = f"Zapytanie (CommandType={option}): {exc}"
                continue
            fields = [rs.Fields(i).Name for i in range(rs.Fields.Count)]
            name_field = next((f for f in fields if f.lower() in ("name", "nazwa", "filename", "linkfilename", "fileleafref")), None)
            files = []
            while not rs.EOF:
                files.append({"nazwa": str(rs.Fields(name_field).Value) if name_field else "?"})
                rs.MoveNext()
            rs.Close()
            conn.Close()
            res.status = "DZIALA"
            res.pliki = files
            res.szczegoly += [f"Połączenie: {conn_str}", f"CommandType: {option}",
                              f"Kolumny: {', '.join(fields)}", f"Wierszy: {len(files)}",
                              "Uwaga: provider zwraca listę plików (metadane), nie ich zawartość."]
            return res
        conn.Close()
    res.status = "NIE DZIALA"
    res.szczegoly.append(last_error)
    if "provider" in last_error.lower() and ("not found" in last_error.lower() or "nie można" in last_error.lower()):
        res.szczegoly.append("Provider niezarejestrowany dla tej bitowości – spróbuj Pythona o bitowości Office.")
    return res


def test_webdav(ld: ListDef, timeout: float = 60) -> Result:
    unc = f"\\\\{ld.host}@SSL\\DavWWWRoot" + ld.root_folder.replace("/", "\\")
    res = Result("C. WebDAV (Otwórz w Eksploratorze)")
    res.szczegoly.append(f"Ścieżka: {unc}")
    if os.name != "nt":
        res.status = "POMINIETO"
        res.szczegoly.append("Tylko Windows")
        return res
    box: dict[str, Any] = {}

    def run() -> None:
        try:
            box["names"] = [n for n in os.listdir(unc) if os.path.isfile(os.path.join(unc, n))]
        except Exception as exc:
            box["error"] = f"{type(exc).__name__}: {exc}"

    t = threading.Thread(target=run, daemon=True)
    t.start()
    t.join(timeout)
    if t.is_alive():
        res.status = "NIE DZIALA"
        res.szczegoly.append(f"Brak odpowiedzi w {timeout:.0f} s (usługa WebClient wyłączona albo zablokowana)")
    elif "error" in box:
        res.status = "NIE DZIALA"
        res.szczegoly.append(box["error"])
    else:
        res.status = "DZIALA"
        res.pliki = [{"nazwa": n, "sciezka": os.path.join(unc, n)} for n in box["names"]]
        res.szczegoly.append(f"Plików: {len(res.pliki)}")
    return res


def test_download(url: str) -> Result:
    res = Result("D. Bezpośrednie pobranie jednego pliku (HTTP, SSO Windows)")
    if not url:
        res.status = "POMINIETO"
        res.szczegoly.append("Podaj --plik-url (w przeglądarce: … przy pliku → Kopiuj link / Skopiuj adres pobierania)")
        return res
    try:
        with _session().get(url, timeout=300, stream=True) as resp:
            res.szczegoly += [d for d in _describe(resp) if not d.startswith("Tytuł")]
            ctype = resp.headers.get("Content-Type", "").lower()
            if resp.status_code == 200 and "html" not in ctype:
                size = 0
                with tempfile.TemporaryFile() as tmp:  # zawartość nie jest zachowywana
                    for chunk in resp.iter_content(1 << 20):
                        size += len(chunk)
                        tmp.write(chunk)
                res.status = "DZIALA"
                res.szczegoly.append(f"Pobrano {size:,} bajtów (plik tymczasowy usunięty)".replace(",", " "))
            else:
                res.status = "NIE DZIALA"
                if "html" in ctype:
                    m = re.search(r"<title[^>]*>(.*?)</title>", resp.text[:20000], re.IGNORECASE | re.DOTALL)
                    if m:
                        res.szczegoly.append("Tytuł strony: " + " ".join(m.group(1).split())[:120])
    except Exception as exc:
        res.status = "NIE DZIALA"
        res.szczegoly.append(f"{type(exc).__name__}: {exc}")
    return res


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Test dostępu do biblioteki RABIT na SharePoint")
    g = p.add_mutually_exclusive_group()
    g.add_argument("--lista-xml", help="plik z tekstem polecenia <LIST>…</LIST> z połączenia Excela")
    g.add_argument("--iqy", help="plik .iqy z „Eksportu do Excela”")
    p.add_argument("--plik-url", default="", help="link do jednego pliku z folderu (test D)")
    p.add_argument("--raport", default="raport_dostepu_rabit.json")
    args = p.parse_args(argv)

    if args.iqy:
        ld = parse_iqy(open(args.iqy, encoding="utf-8-sig", errors="replace").read())
    elif args.lista_xml:
        ld = parse_list_xml(open(args.lista_xml, encoding="utf-8-sig").read())
    else:
        ld = parse_list_xml(DEFAULT_LIST_XML)

    print(f"Witryna : {ld.web}\nLista   : {ld.list_id}  widok {ld.view_id}\nFolder  : {ld.root_folder}\n")
    results = [test_owssvr(ld), test_oledb(ld), test_webdav(ld)]
    listed = next((r for r in results if r.status == "DZIALA" and r.pliki and r.pliki[0].get("url")), None)
    results.append(test_download(args.plik_url or (listed.pliki[0]["url"] if listed else "")))

    for r in results:
        print(f"[{r.status:^14}] {r.metoda}")
        for line in r.szczegoly:
            print(f"                 {line}")
        for f in r.pliki[:10]:
            print(f"                 • {f['nazwa']}")
        if len(r.pliki) > 10:
            print(f"                 … i {len(r.pliki) - 10} więcej")
        print()

    ok = {r.metoda[0] for r in results if r.status == "DZIALA"}
    if "C" in ok or (ok & {"A", "B"} and "D" in ok):
        print("WNIOSEK: automatyczne pobieranie jest możliwe – przekaż raport, dołączę tę metodę do importu.")
    elif ok & {"A", "B"}:
        print("WNIOSEK: działa lista plików, ale nie pobieranie – aplikacja może pokazać, co nowego pobrać ręcznie.")
    elif "D" in ok:
        print("WNIOSEK: działa pobieranie znanego pliku – można pobierać pliki o stałych nazwach / z listy linków.")
    else:
        print("WNIOSEK: żadna metoda automatyczna nie działa – zostaje ręczne pobieranie do 00_Global\\RABIT\\Do_importu.")

    with open(args.raport, "w", encoding="utf-8") as fh:
        json.dump({"witryna": ld.web, "folder": ld.root_folder, "python": platform.python_version(),
                   "bitowosc": struct.calcsize("P") * 8, "wyniki": [r.__dict__ for r in results]},
                  fh, ensure_ascii=False, indent=2)
    print(f"Raport: {os.path.abspath(args.raport)} (bez zawartości plików – przejrzyj nazwy przed wysłaniem)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
