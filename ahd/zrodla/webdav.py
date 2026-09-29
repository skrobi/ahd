"""RABIT na SharePoint przez WebDAV (jak „Otwórz w Eksploratorze”) – rozwiązanie docelowe.

Windows (usługa WebClient) udostępnia bibliotekę jako ścieżkę UNC:
    \\\\host@SSL\\DavWWWRoot\\sites\\<witryna>\\<biblioteka>\\<folder>
Logowanie odbywa się kontem użytkownika Windows. Ścieżkę czyta zwykłe źródło folderowe.

Ograniczenie: WebClient domyślnie nie pobiera plików większych niż ok. 50 MB
(rejestr: HKLM\\SYSTEM\\CurrentControlSet\\Services\\WebClient\\Parameters\\FileSizeLimitInBytes,
zmiana wymaga administratora).
"""

from __future__ import annotations

from urllib.parse import parse_qs, unquote, urlsplit

SIZE_LIMIT_HINT = (
    "Plik przekracza limit usługi WebClient (domyślnie ok. 50 MB). Administrator może zwiększyć "
    "FileSizeLimitInBytes w HKLM\\SYSTEM\\CurrentControlSet\\Services\\WebClient\\Parameters; "
    "do tego czasu pobierz ten plik ręcznie w przeglądarce do folderu Do_importu."
)


def unc_from_url(url: str) -> str:
    """Zamienia link do folderu SharePoint (skopiowany z przeglądarki) na ścieżkę WebDAV UNC.

    Obsługuje bezpośredni adres folderu oraz widok listy (AllItems.aspx?RootFolder=… / ?id=…).
    Ścieżka UNC podana wprost jest zwracana bez zmian.
    """
    url = url.strip()
    if url.startswith("\\\\"):
        return url
    parts = urlsplit(url)
    if parts.scheme not in ("http", "https") or not parts.netloc:
        raise ValueError(f"To nie jest link SharePoint ani ścieżka UNC: {url!r}")
    query = parse_qs(parts.query)
    if "RootFolder" in query:
        folder = query["RootFolder"][0]
    elif "id" in query:
        folder = query["id"][0]
    else:
        folder = unquote(parts.path).split("/Forms/")[0]
    host = parts.netloc + ("@SSL" if parts.scheme == "https" else "")
    return f"\\\\{host}\\DavWWWRoot" + ("/" + folder.strip("/")).replace("/", "\\")
