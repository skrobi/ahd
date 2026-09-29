"""Landing Zone: kopia oryginałów, hash SHA-256, manifest przebiegu."""

from __future__ import annotations

import datetime as dt
import getpass
import hashlib
import json
from pathlib import Path
from typing import Any


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run_id(zakres: str, okres: str, tydzien: int | None) -> str:
    """R-<Zakres>-<RRRR-MM>-T<NN> (tydzień) albo R-<Zakres>-<RRRR-MM>-Z (zamknięcie)."""
    return f"R-{zakres}-{okres}-" + (f"T{tydzien:02d}" if tydzien is not None else "Z")


def run_dir(root: Path, zakres: str, okres: str, rid: str) -> Path:
    return Path(root) / zakres / okres / rid


def previous_hashes(root: Path, zakres: str, exclude: Path | None = None) -> dict[str, set[str]]:
    """Hashe plików z wcześniejszych manifestów zakresu: nazwa pliku -> zbiór hashy."""
    known: dict[str, set[str]] = {}
    base = Path(root) / zakres
    if not base.exists():
        return known
    for manifest in base.glob("*/*/manifest.json"):
        if exclude and manifest.parent == exclude:
            continue
        try:
            data = json.loads(manifest.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        for rec in data.get("pliki", []):
            known.setdefault(rec["plik"], set()).add(rec["sha256"])
    return known


def change_status(name: str, sha: str, known: dict[str, set[str]]) -> str:
    if sha in known.get(name, set()):
        return "bez zmian"
    if any(sha in hashes for hashes in known.values()):
        return "bez zmian (inna nazwa)"
    return "zmieniony" if name in known else "nowy"


def write_manifest(directory: Path, manifest: dict[str, Any]) -> Path:
    path = directory / "manifest.json"
    tmp = path.with_suffix(".json.part")
    tmp.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    tmp.replace(path)
    return path


def new_manifest(rid: str, zakres: str, okres: str, zrodlo: str) -> dict[str, Any]:
    return {
        "przebieg": rid,
        "zakres": zakres,
        "okres": okres,
        "zrodlo": zrodlo,
        "uzytkownik": getpass.getuser(),
        "start": dt.datetime.now().astimezone().isoformat(timespec="seconds"),
        "pliki": [],
        "zestawy": [],
    }
