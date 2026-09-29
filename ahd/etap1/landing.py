"""Landing Zone: archiwum oryginałów plików (bez podziału na zakresy)."""

from __future__ import annotations

import datetime as dt
import hashlib
import uuid
from pathlib import Path


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def new_batch_id(now: dt.datetime | None = None) -> str:
    """Identyfikator importu: IMP-RRRRMMDD-HHMMSS-xxxx (czytelny i unikalny)."""
    now = now or dt.datetime.now()
    return f"IMP-{now:%Y%m%d-%H%M%S}-{uuid.uuid4().hex[:4]}"


def batch_dir(root: Path, batch_id: str, now: dt.datetime | None = None) -> Path:
    """<LandingZone>/<RRRR-MM-DD>/<BatchId>/ – pliki nowe (zaimportowane) w danym imporcie."""
    now = now or dt.datetime.now()
    return Path(root) / f"{now:%Y-%m-%d}" / batch_id


def temp_dir(root: Path, batch_id: str) -> Path:
    """Pliki pobrane do sprawdzenia hasha; duplikaty są stąd usuwane."""
    return Path(root) / "_tmp" / batch_id


def unique_path(directory: Path, name: str) -> Path:
    dest = directory / name
    i = 2
    while dest.exists():
        dest = directory / f"{Path(name).stem} ({i}){Path(name).suffix}"
        i += 1
    return dest
