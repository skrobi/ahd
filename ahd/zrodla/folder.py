"""Źródło plików w folderze lokalnym lub sieciowym (UNC, folder synchronizowany OneDrive)."""

from __future__ import annotations

import datetime as dt
import shutil
from pathlib import Path
from typing import Iterator

from .sharepoint import RemoteFile


class FolderSource:
    def __init__(self, root: str | Path) -> None:
        self.root = Path(root)
        if not self.root.is_dir():
            raise FileNotFoundError(f"Folder nie istnieje: {self.root}")

    def walk(self, recursive: bool = False) -> Iterator[RemoteFile]:
        pattern = "**/*" if recursive else "*"
        for path in sorted(self.root.glob(pattern), key=lambda p: str(p).lower()):
            # Pliki blokady Excela (~$nazwa.xlsx) i pliki tymczasowe pomijamy.
            if not path.is_file() or path.name.startswith("~$") or path.suffix == ".part":
                continue
            stat = path.stat()
            yield RemoteFile(
                name=path.name,
                server_relative_url=str(path),
                size=stat.st_size,
                modified=dt.datetime.fromtimestamp(stat.st_mtime, dt.timezone.utc).isoformat(timespec="seconds"),
            )

    def download(self, remote: RemoteFile, dest: Path) -> Path:
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp = dest.with_name(dest.name + ".part")
        shutil.copy2(remote.server_relative_url, tmp)
        tmp.replace(dest)
        return dest
