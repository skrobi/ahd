"""Źródło plików w folderze: lokalnym, sieciowym (UNC) albo WebDAV (ścieżka UNC do SharePoint).

Wskazać można folder (np. `00_Global\\RABIT\\Do_importu`) albo pojedynczy plik
(np. ręcznie pobrany z przeglądarki).
"""

from __future__ import annotations

import datetime as dt
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Iterator


@dataclass(frozen=True)
class RemoteFile:
    name: str
    path: str
    size: int
    modified: str  # ISO 8601, UTC


def _skip(name: str) -> bool:
    # Pliki blokady Excela (~$nazwa.xlsx), pliki tymczasowe i ukryte.
    return name.startswith(("~$", ".")) or name.endswith(".part")


class FolderSource:
    def __init__(self, root: str | Path) -> None:
        self.root = Path(root)
        if not self.root.exists():
            raise FileNotFoundError(f"Folder lub plik nie istnieje: {self.root}")

    def walk(self, recursive: bool = False) -> Iterator[RemoteFile]:
        if self.root.is_file():
            yield self._file(self.root)
            return
        pattern = "**/*" if recursive else "*"
        for path in sorted(self.root.glob(pattern), key=lambda p: str(p).lower()):
            if path.is_file() and not _skip(path.name):
                yield self._file(path)

    @staticmethod
    def _file(path: Path) -> RemoteFile:
        stat = path.stat()
        return RemoteFile(
            name=path.name,
            path=str(path),
            size=stat.st_size,
            modified=dt.datetime.fromtimestamp(stat.st_mtime, dt.timezone.utc).isoformat(timespec="seconds"),
        )

    def download(self, remote: RemoteFile, dest: Path) -> Path:
        """Kopiuje plik (z zachowaniem daty modyfikacji); do końca kopiowania pod nazwą .part."""
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp = dest.with_name(dest.name + ".part")
        shutil.copy2(remote.path, tmp)
        tmp.replace(dest)
        return dest
