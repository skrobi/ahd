"""Źródło plików w folderze lokalnym lub sieciowym – także pliki ręcznie pobrane z przeglądarki.

Archiwa ZIP (SharePoint pakuje do ZIP pobranie kilku plików naraz) są czytane bez rozpakowywania:
każdy plik w archiwum jest traktowany jak osobny plik źródłowy. Wskazać można folder
(np. „Do_importu”) albo bezpośrednio plik .zip.
"""

from __future__ import annotations

import datetime as dt
import shutil
import zipfile
from pathlib import Path, PurePosixPath
from typing import Iterator

from .sharepoint import RemoteFile

ZIP_SEP = "!"  # ścieżka pliku w archiwum: C:\...\paczka.zip!folder/plik.csv


def _skip(name: str) -> bool:
    # Pliki blokady Excela (~$nazwa.xlsx), pliki tymczasowe i ukryte.
    return name.startswith(("~$", ".")) or name.endswith(".part")


class FolderSource:
    def __init__(self, root: str | Path) -> None:
        self.root = Path(root)
        if not (self.root.is_dir() or (self.root.is_file() and zipfile.is_zipfile(self.root))):
            raise FileNotFoundError(f"Folder lub plik ZIP nie istnieje: {self.root}")

    def walk(self, recursive: bool = False) -> Iterator[RemoteFile]:
        if self.root.is_file():
            yield from self._zip_entries(self.root)
            return
        pattern = "**/*" if recursive else "*"
        for path in sorted(self.root.glob(pattern), key=lambda p: str(p).lower()):
            if not path.is_file() or _skip(path.name):
                continue
            if path.suffix.lower() == ".zip" and zipfile.is_zipfile(path):
                yield from self._zip_entries(path)
                continue
            stat = path.stat()
            yield RemoteFile(
                name=path.name,
                server_relative_url=str(path),
                size=stat.st_size,
                modified=dt.datetime.fromtimestamp(stat.st_mtime, dt.timezone.utc).isoformat(timespec="seconds"),
            )

    @staticmethod
    def _zip_entries(archive: Path) -> Iterator[RemoteFile]:
        with zipfile.ZipFile(archive) as zf:
            for info in sorted(zf.infolist(), key=lambda i: i.filename.lower()):
                name = PurePosixPath(info.filename).name
                if info.is_dir() or not name or _skip(name):
                    continue
                yield RemoteFile(
                    name=name,
                    server_relative_url=f"{archive}{ZIP_SEP}{info.filename}",
                    size=info.file_size,
                    modified=dt.datetime(*info.date_time).isoformat(timespec="seconds"),
                )

    def download(self, remote: RemoteFile, dest: Path) -> Path:
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp = dest.with_name(dest.name + ".part")
        archive, sep, member = remote.server_relative_url.partition(ZIP_SEP)
        if sep and zipfile.is_zipfile(archive):
            with zipfile.ZipFile(archive) as zf, zf.open(member) as src, tmp.open("wb") as out:
                shutil.copyfileobj(src, out, length=1 << 20)
        else:
            shutil.copy2(remote.server_relative_url, tmp)
        tmp.replace(dest)
        return dest
