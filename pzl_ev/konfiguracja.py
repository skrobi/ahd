"""Konfiguracja importu RABIT: źródła rozpoznawane po prefiksie nazwy pliku i źródła wymagane przez projekty.

Dwa pliki CSV (separator `;`, UTF-8, edytowalne w Excelu) w katalogu konfiguracji:

    zrodla_rabit.csv      Prefiks;KodZrodla;Opis
                          ACTUALS_PAF;ACTUALS_PAF;Koszty rzeczywiste PAF

    projekty_zrodla.csv   Projekt;KodZrodla
                          PAF-001;ACTUALS_PAF

Plik mówi, czym jest (prefiks → źródło), projekt mówi, czego potrzebuje (projekt → źródła).
Wiersze zaczynające się od `#` są pomijane.
"""

from __future__ import annotations

import csv
import io
import os
from dataclasses import dataclass
from pathlib import Path

SOURCES_FILE = "zrodla_rabit.csv"
PROJECTS_FILE = "projekty_zrodla.csv"


class ConfigError(Exception):
    """Błąd w plikach konfiguracji – opis wskazuje plik i wiersz."""


@dataclass(frozen=True)
class SourceDef:
    prefix: str
    code: str
    description: str = ""


def default_dir() -> Path:
    return Path(os.environ.get("PZL_EV_KONFIGURACJA", "konfiguracja"))


def _rows(path: Path, columns: list[str]) -> list[tuple[int, dict[str, str]]]:
    if not path.exists():
        raise ConfigError(f"Brak pliku konfiguracji: {path}")
    text = path.read_text(encoding="utf-8-sig")
    lines = [ln for ln in text.splitlines() if ln.strip() and not ln.lstrip().startswith("#")]
    if not lines:
        raise ConfigError(f"{path.name}: plik jest pusty")
    reader = csv.DictReader(io.StringIO("\n".join(lines)), delimiter=";")
    header = [h.strip() for h in reader.fieldnames or []]
    missing = [c for c in columns if c not in header]
    if missing:
        raise ConfigError(f"{path.name}: brak kolumn {', '.join(missing)} (są: {', '.join(header)})")
    out = []
    for n, row in enumerate(reader, start=2):
        out.append((n, {k.strip(): (v or "").strip() for k, v in row.items() if k}))
    return out


def load_sources(directory: str | Path) -> list[SourceDef]:
    path = Path(directory) / SOURCES_FILE
    defs: list[SourceDef] = []
    seen: dict[str, int] = {}
    for n, row in _rows(path, ["Prefiks", "KodZrodla"]):
        prefix, code = row["Prefiks"], row["KodZrodla"]
        if not prefix or not code:
            raise ConfigError(f"{path.name}, wiersz {n}: Prefiks i KodZrodla są wymagane")
        key = prefix.lower()
        if key in seen:
            raise ConfigError(f"{path.name}, wiersz {n}: prefiks '{prefix}' zdefiniowany już w wierszu {seen[key]}")
        seen[key] = n
        defs.append(SourceDef(prefix=prefix, code=code, description=row.get("Opis", "")))
    return defs


def load_project_requirements(directory: str | Path, sources: list[SourceDef]) -> dict[str, list[str]]:
    """Projekt → lista wymaganych kodów źródeł (w kolejności z pliku)."""
    path = Path(directory) / PROJECTS_FILE
    known = {s.code for s in sources}
    required: dict[str, list[str]] = {}
    for n, row in _rows(path, ["Projekt", "KodZrodla"]):
        project, code = row["Projekt"], row["KodZrodla"]
        if not project or not code:
            raise ConfigError(f"{path.name}, wiersz {n}: Projekt i KodZrodla są wymagane")
        if code not in known:
            raise ConfigError(f"{path.name}, wiersz {n}: źródło '{code}' nie jest zdefiniowane w {SOURCES_FILE}")
        codes = required.setdefault(project, [])
        if code not in codes:
            codes.append(code)
    return required


def match_source(filename: str, sources: list[SourceDef]) -> SourceDef | None:
    """Źródło pliku po prefiksie nazwy (bez rozróżniania wielkości liter); wygrywa najdłuższy prefiks."""
    name = Path(filename).name.lower()
    hits = [s for s in sources if name.startswith(s.prefix.lower())]
    return max(hits, key=lambda s: len(s.prefix)) if hits else None
