"""Dostęp do biblioteki SharePoint przez WebDAV (jak „Otwórz w Eksploratorze”).

Windows (usługa WebClient) udostępnia bibliotekę jako ścieżkę UNC:
    \\\\host@SSL\\DavWWWRoot\\sites\\<witryna>\\<biblioteka>\\<folder>
Logowanie odbywa się kontem użytkownika Windows. Ścieżkę czyta zwykłe źródło folderowe.

Ograniczenie: WebClient domyślnie nie pobiera plików większych niż ok. 50 MB
(rejestr: HKLM\\SYSTEM\\CurrentControlSet\\Services\\WebClient\\Parameters\\FileSizeLimitInBytes,
zmiana wymaga administratora).
"""

from __future__ import annotations

from urllib.parse import urlsplit

from .sharepoint import parse_sharepoint_url

SIZE_LIMIT_HINT = (
    "Plik przekracza limit usługi WebClient (domyślnie ok. 50 MB). Administrator może zwiększyć "
    "FileSizeLimitInBytes w HKLM\\SYSTEM\\CurrentControlSet\\Services\\WebClient\\Parameters; "
    "do tego czasu pobierz ten plik ręcznie w przeglądarce."
)


def unc_from_url(url: str) -> str:
    """Zamienia link do folderu SharePoint na ścieżkę WebDAV UNC."""
    if url.startswith("\\\\"):
        return url
    loc = parse_sharepoint_url(url)
    parts = urlsplit(loc.site_url)
    host = parts.netloc + ("@SSL" if parts.scheme == "https" else "")
    return f"\\\\{host}\\DavWWWRoot" + loc.folder.replace("/", "\\")
