"""Dostęp do biblioteki dokumentów SharePoint (wersja serwerowa) przez REST API.

Uwierzytelnienie odbywa się kontem zalogowanego użytkownika Windows (SSO:
Kerberos/NTLM przez SSPI) – moduł nie przechowuje ani nie przyjmuje haseł
w konfiguracji.
"""

from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterator
from urllib.parse import parse_qs, quote, unquote, urlsplit

import requests

# Foldery systemowe biblioteki, które nie zawierają plików użytkownika.
SYSTEM_FOLDERS = {"Forms"}


class SharePointError(Exception):
    """Błąd komunikacji z SharePoint zrozumiały dla użytkownika."""


@dataclass(frozen=True)
class SharePointLocation:
    site_url: str  # np. https://host/sites/RabbitReporting
    folder: str  # ścieżka względna serwera, np. /sites/RabbitReporting/Shared Documents/E456659


@dataclass(frozen=True)
class RemoteFile:
    name: str
    server_relative_url: str
    size: int
    modified: str  # ISO 8601 z SharePoint (UTC)


def parse_sharepoint_url(url: str) -> SharePointLocation:
    """Wyznacza adres witryny i folderu z linku skopiowanego z przeglądarki.

    Obsługuje widok listy (``AllItems.aspx?RootFolder=...``), nowy widok
    (``AllItems.aspx?id=...``) oraz bezpośredni adres folderu.
    """
    parts = urlsplit(url.strip())
    if parts.scheme not in ("http", "https") or not parts.netloc:
        raise ValueError(f"To nie jest adres SharePoint: {url!r}")

    query = parse_qs(parts.query)
    path = unquote(parts.path)
    if "RootFolder" in query:
        folder = query["RootFolder"][0]
    elif "id" in query:
        folder = query["id"][0]
    elif "/Forms/" in path:
        # Widok biblioteki bez wskazanego folderu – korzeń biblioteki.
        folder = path.split("/Forms/")[0]
    else:
        folder = path

    folder = "/" + folder.strip("/")
    segments = folder.split("/")
    # /sites/<nazwa>/... albo /teams/<nazwa>/... – w przeciwnym razie witryna główna.
    if len(segments) > 2 and segments[1].lower() in ("sites", "teams"):
        site_path = "/".join(segments[:3])
    else:
        site_path = ""
    return SharePointLocation(site_url=f"{parts.scheme}://{parts.netloc}{site_path}", folder=folder)


def build_auth(kind: str) -> Any:
    """Tworzy obiekt uwierzytelnienia dla ``requests``.

    - ``sso``  – bieżący użytkownik Windows (Kerberos/NTLM przez SSPI), bez hasła,
    - ``ntlm`` – login i hasło wpisywane w konsoli (niezapisywane), awaryjnie,
    - ``none`` – bez uwierzytelnienia (testy).
    """
    if kind == "none":
        return None
    if kind == "sso":
        try:
            from requests_negotiate_sspi import HttpNegotiateAuth
        except ImportError as exc:
            raise SharePointError(
                "Logowanie SSO wymaga systemu Windows i pakietu requests-negotiate-sspi "
                "(pip install -r requirements.txt)."
            ) from exc
        return HttpNegotiateAuth()
    if kind == "ntlm":
        import getpass

        try:
            from requests_ntlm import HttpNtlmAuth
        except ImportError as exc:
            raise SharePointError("Brak pakietu requests-ntlm (pip install requests-ntlm).") from exc
        default_user = os.environ.get("USERDOMAIN", "") + "\\" + os.environ.get("USERNAME", "")
        user = input(f"Użytkownik [{default_user}]: ").strip() or default_user
        return HttpNtlmAuth(user, getpass.getpass("Hasło (nie jest zapisywane): "))
    raise ValueError(f"Nieznany sposób logowania: {kind}")


def _safe_url(url: str) -> str:
    """Adres bez parametrów zapytania (mogą zawierać tokeny logowania)."""
    parts = urlsplit(url)
    return f"{parts.scheme}://{parts.netloc}{parts.path}"


def describe_response(resp: requests.Response) -> str:
    """Opis odpowiedzi do diagnostyki – bez treści danych, tylko metadane i tytuł strony."""
    lines = []
    for r in [*resp.history, resp]:
        lines.append(f"  HTTP {r.status_code}  {_safe_url(r.url)}")
    ctype = resp.headers.get("Content-Type", "brak")
    lines.append(f"  Typ odpowiedzi: {ctype}")
    for header in ("MicrosoftSharePointTeamServices", "Server", "WWW-Authenticate"):
        if header in resp.headers:
            lines.append(f"  {header}: {resp.headers[header]}")
    if "html" in ctype.lower():
        import re

        m = re.search(r"<title[^>]*>(.*?)</title>", resp.text[:20000], re.IGNORECASE | re.DOTALL)
        if m:
            lines.append(f"  Tytuł strony: {' '.join(m.group(1).split())[:120]}")
    return "\n".join(lines)


def _hint(resp: requests.Response, site_url: str) -> str:
    site_host = urlsplit(site_url).netloc.lower()
    hosts = {urlsplit(r.url).netloc.lower() for r in [*resp.history, resp]}
    text = " ".join(_safe_url(r.url).lower() for r in [*resp.history, resp])
    ctype = resp.headers.get("Content-Type", "").lower()
    version = resp.headers.get("MicrosoftSharePointTeamServices", "")
    if hosts - {site_host} or any(k in text for k in ("adfs", "login", "signin", "saml", "wsfed", "sso", "auth")):
        return ("Serwer przekierowuje na stronę logowania w przeglądarce (np. ADFS / logowanie korporacyjne). "
                "Logowanie kontem Windows (SSO) nie wystarcza dla tego adresu. Obejście na teraz: "
                "zsynchronizuj bibliotekę przez OneDrive albo otwórz ją w Eksploratorze i użyj --folder.")
    if version.startswith("14."):
        return "SharePoint 2010 – brak API REST /_api. Użyj --folder (synchronizacja / Eksplorator)."
    if "xml" in ctype or "atom" in ctype:
        return "Serwer zwrócił XML zamiast JSON – przekaż ten wynik, dostosuję moduł."
    if "html" in ctype:
        return "Serwer zwrócił stronę HTML zamiast danych – przekaż ten wynik (bez treści plików), dostosuję moduł."
    return "Nieoczekiwana odpowiedź serwera – przekaż ten wynik, dostosuję moduł."


def _quote_path(path: str) -> str:
    # Apostrof w literale OData zapisuje się podwójnie; reszta jak w URL.
    return quote(path.replace("'", "''"), safe="/")


class SharePointClient:
    def __init__(
        self,
        site_url: str,
        auth: Any = None,
        session: requests.Session | None = None,
        timeout: float = 120,
        verify: bool | str = True,
    ) -> None:
        self.site_url = site_url.rstrip("/")
        self.session = session or requests.Session()
        self.session.auth = auth
        self.session.headers.update({"Accept": "application/json;odata=verbose"})
        self.timeout = timeout
        self.verify = verify

    def _get(self, api_path: str, stream: bool = False) -> requests.Response:
        url = self.site_url + api_path
        try:
            resp = self.session.get(url, timeout=self.timeout, verify=self.verify, stream=stream)
        except requests.exceptions.SSLError as exc:
            raise SharePointError(
                "Błąd certyfikatu TLS. Zainstaluj pakiet 'truststore' (używa magazynu certyfikatów "
                "Windows) albo wskaż plik CA opcją --ca-bundle."
            ) from exc
        except requests.exceptions.ConnectionError as exc:
            raise SharePointError(f"Brak połączenia z {self.site_url} – sprawdź sieć / VPN / proxy.") from exc
        if resp.status_code in (401, 403):
            methods = resp.headers.get("WWW-Authenticate", "brak")
            raise SharePointError(
                f"Odmowa dostępu (HTTP {resp.status_code}) do {_safe_url(url)}. "
                f"Serwer oferuje metody logowania: {methods}. "
                "Sprawdź, czy masz dostęp do folderu w przeglądarce; jeśli tak, a SSO nie działa, "
                "spróbuj --auth ntlm.\n" + describe_response(resp)
            )
        if resp.status_code == 404:
            raise SharePointError(f"Nie znaleziono zasobu (HTTP 404): {_safe_url(url)}\n" + describe_response(resp))
        if resp.status_code >= 400:
            raise SharePointError(f"Błąd SharePoint HTTP {resp.status_code}\n" + describe_response(resp))
        return resp

    def _json(self, api_path: str) -> dict[str, Any]:
        resp = self._get(api_path)
        ctype = resp.headers.get("Content-Type", "").lower()
        try:
            if "json" not in ctype:
                raise ValueError(ctype)
            return resp.json()
        except ValueError:
            raise SharePointError(
                "SharePoint nie zwrócił danych w formacie JSON.\n" + describe_response(resp) + "\n" + _hint(resp, self.site_url)
            ) from None

    @staticmethod
    def _results(data: dict[str, Any], key: str) -> list[dict[str, Any]]:
        # odata=verbose: {"d": {"Files": {"results": [...]}}}; nometadata: {"Files": [...]}.
        body = data.get("d", data)
        value = body.get(key, [])
        if isinstance(value, dict):
            value = value.get("results", [])
        return list(value)

    def check(self) -> str:
        """Sprawdza połączenie i logowanie; zwraca nazwę zalogowanego użytkownika."""
        data = self._json("/_api/web/currentuser")
        body = data.get("d", data)
        return body.get("LoginName") or body.get("Title") or "?"

    def list_folder(self, folder: str) -> tuple[list[RemoteFile], list[str]]:
        api = f"/_api/web/GetFolderByServerRelativeUrl('{_quote_path(folder)}')?$expand=Files,Folders"
        data = self._json(api)
        files = [
            RemoteFile(
                name=f["Name"],
                server_relative_url=f["ServerRelativeUrl"],
                size=int(f.get("Length") or 0),
                modified=f.get("TimeLastModified", ""),
            )
            for f in self._results(data, "Files")
        ]
        folders = [f["ServerRelativeUrl"] for f in self._results(data, "Folders") if f.get("Name") not in SYSTEM_FOLDERS]
        return files, folders

    def walk(self, folder: str, recursive: bool = False) -> Iterator[RemoteFile]:
        files, folders = self.list_folder(folder)
        yield from sorted(files, key=lambda f: f.name.lower())
        if recursive:
            for sub in sorted(folders):
                yield from self.walk(sub, recursive=True)

    def download(self, remote: RemoteFile, dest: Path) -> Path:
        """Pobiera plik strumieniowo; do czasu zakończenia zapisuje pod nazwą .part."""
        api = f"/_api/web/GetFileByServerRelativeUrl('{_quote_path(remote.server_relative_url)}')/$value"
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp = dest.with_name(dest.name + ".part")
        with self._get(api, stream=True) as resp, tmp.open("wb") as fh:
            for chunk in resp.iter_content(chunk_size=1 << 20):
                fh.write(chunk)
        tmp.replace(dest)
        return dest
