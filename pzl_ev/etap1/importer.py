"""Import plików RABIT do bazy.

Plik mówi, czym jest: źródło rozpoznawane jest po **prefiksie nazwy** (konfiguracja `zrodla_rabit.csv`).
Dla każdego pliku w źródle:
1. brak pasującego prefiksu → „nierozpoznany” (nie jest importowany; po dopisaniu prefiksu zostanie
   zaimportowany przy kolejnym uruchomieniu),
2. metadane (ścieżka, rozmiar, data modyfikacji) jak przy wcześniejszym imporcie → pomiń bez kopiowania,
3. skopiuj do katalogu tymczasowego, policz SHA-256; hash już w bazie → duplikat (ten fizyczny plik
   był już zaimportowany),
4. nowy hash → przenieś do Landing Zone, zarejestruj plik ze źródłem i załaduj wiersze.

Każdy rozpoznany plik jest importowany samodzielnie – także pliki tego samego źródła
(ACTUALS_PAF_01, _02, _03) o różnych układach kolumn.
"""

from __future__ import annotations

import datetime as dt
import getpass
import platform
import shutil
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable

from pzl_ev import __version__
from pzl_ev.baza import Database, DuplicateFile
from pzl_ev.konfiguracja import SourceDef, match_source
from pzl_ev.zrodla.folder import RemoteFile

from . import inspekcja, landing

Downloader = Callable[[RemoteFile, Path], Path]


def fmt_int(n: int) -> str:
    return f"{n:,}".replace(",", " ")


@dataclass
class ImportResult:
    batch_id: str
    records: list[dict[str, Any]] = field(default_factory=list)

    def count(self, decision: str) -> int:
        return sum(1 for r in self.records if r["decyzja"] == decision)

    @property
    def errors(self) -> int:
        return self.count("blad")


def run_import(
    files: Iterable[RemoteFile],
    download: Downloader,
    sources: list[SourceDef],
    db: Database,
    landing_root: Path,
    source_desc: str,
    full_check: bool = False,
    progress: Callable[[str], None] = print,
) -> ImportResult:
    now = dt.datetime.now()
    batch_id = landing.new_batch_id(now)
    user = getpass.getuser()
    db.start_batch(batch_id, source_desc, user, platform.node(), __version__)
    result = ImportResult(batch_id)
    tmp = landing.temp_dir(landing_root, batch_id)
    target = landing.batch_dir(landing_root, batch_id, now)
    files = list(files)

    for i, f in enumerate(files, 1):
        rec: dict[str, Any] = {"plik": f.name, "zrodlo": f.path, "rozmiar": f.size, "zmodyfikowany": f.modified}
        prefix = f"[{i}/{len(files)}] {f.name}"
        source = match_source(f.name, sources)
        try:
            if source is None:
                rec.update(decyzja="nierozpoznany", opis="brak pasującego prefiksu w zrodla_rabit.csv")
            elif not full_check and (known := db.unchanged_by_metadata(f.path, f.size, f.modified)):
                rec.update(decyzja="pominiety (metadane)", sha256=known, kod_zrodla=source.code,
                           opis="bez zmian od poprzedniego importu")
            else:
                rec["kod_zrodla"] = source.code
                rec.update(_download_and_import(f, download, source, db, tmp, target, batch_id, user))
        except Exception as exc:  # pojedynczy plik nie przerywa importu
            rec.update(decyzja="blad", opis=f"{type(exc).__name__}: {exc}")
        db.record_seen(batch_id, f.name, f.path, f.size, f.modified, rec.get("sha256"), rec["decyzja"], rec.get("opis", ""))
        progress(f"{prefix}: {rec['decyzja']}" + (f" – {rec['opis']}" if rec.get("opis") else ""))
        result.records.append(rec)

    shutil.rmtree(tmp, ignore_errors=True)
    try:
        tmp.parent.rmdir()  # usuwa pusty katalog _tmp
    except OSError:
        pass
    imported = result.count("zaimportowany")
    db.finish_batch(batch_id, len(files), imported, len(files) - imported - result.errors, result.errors)
    return result


def _download_and_import(f: RemoteFile, download: Downloader, source: SourceDef, db: Database, tmp: Path,
                         target: Path, batch_id: str, user: str) -> dict[str, Any]:
    tmp.mkdir(parents=True, exist_ok=True)
    local = download(f, landing.unique_path(tmp, f.name))
    sha = landing.sha256_file(local)
    existing = db.file_by_hash(sha)
    if existing:
        local.unlink(missing_ok=True)
        return {"decyzja": "duplikat", "sha256": sha,
                "opis": f"treść już zaimportowana jako {existing['NazwaPliku']} ({existing['Uzytkownik']}, {existing['Zaimportowano']})"}

    info = inspekcja.inspect_file(local)
    if "blad" in info or "uwaga" in info:
        raise ValueError(info.get("blad") or info.get("uwaga"))
    target.mkdir(parents=True, exist_ok=True)
    final = landing.unique_path(target, f.name)
    shutil.move(str(local), final)
    meta = {
        "sha256": sha, "plik": f.name, "zrodlo": f.path, "rozmiar": f.size, "zmodyfikowany": f.modified,
        "landing": str(final), "batch": batch_id, "uzytkownik": user, "typ": info.get("typ"), "arkusz": info.get("arkusz"),
        "kodowanie": info.get("kodowanie"), "separator": info.get("separator"), "kolumny": info.get("kolumny", []),
        "sygnatura": inspekcja.header_signature(info.get("kolumny", [])), "kod_zrodla": source.code,
    }
    try:
        rows = db.import_file(meta, inspekcja.read_rows(final, info))
    except DuplicateFile:
        final.unlink(missing_ok=True)
        return {"decyzja": "duplikat", "sha256": sha, "opis": "w międzyczasie zaimportowany przez inną osobę"}
    except Exception:
        final.unlink(missing_ok=True)
        raise
    return {"decyzja": "zaimportowany", "sha256": sha, "inspekcja": info, "wiersze": rows,
            "opis": f"{source.code}, {fmt_int(rows)} wierszy"}
