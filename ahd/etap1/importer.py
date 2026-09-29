"""Import plików SAP (RABIT) do bazy – bez znajomości zakresu.

Dla każdego pliku w źródle:
1. metadane (ścieżka, rozmiar, data modyfikacji) jak przy wcześniejszym imporcie → pomiń bez pobierania,
2. pobierz do katalogu tymczasowego, policz SHA-256,
3. hash już w bazie → duplikat (plik usunięty z katalogu tymczasowego),
4. nowy hash → przenieś do Landing Zone, zarejestruj plik i załaduj wiersze w postaci surowej.

Przypisanie wierszy do zakresów odbywa się później – w przebiegu zakresu, na podstawie słownika
struktury projektowej.
"""

from __future__ import annotations

import datetime as dt
import getpass
import platform
import shutil
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable

from ahd import __version__
from ahd.baza import Database, DuplicateFile
from ahd.zrodla.sharepoint import RemoteFile

from . import inspekcja, landing

Downloader = Callable[[RemoteFile, Path], Path]


def fmt_int(n: int) -> str:
    return f"{n:,}".replace(",", " ")


@dataclass
class ImportResult:
    batch_id: str
    records: list[dict[str, Any]] = field(default_factory=list)
    sets: list[dict[str, Any]] = field(default_factory=list)

    def count(self, decision: str) -> int:
        return sum(1 for r in self.records if r["decyzja"] == decision)

    @property
    def errors(self) -> int:
        return self.count("blad")


def run_import(
    files: Iterable[RemoteFile],
    download: Downloader,
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
        rec: dict[str, Any] = {"plik": f.name, "zrodlo": f.server_relative_url, "rozmiar": f.size, "zmodyfikowany": f.modified}
        prefix = f"[{i}/{len(files)}] {f.name}"
        try:
            known = None if full_check else db.unchanged_by_metadata(f.server_relative_url, f.size, f.modified)
            if known:
                rec.update(decyzja="pominiety (metadane)", sha256=known, opis="bez zmian od poprzedniego importu")
            else:
                rec.update(_download_and_import(f, download, db, tmp, target, batch_id, user))
        except Exception as exc:  # pojedynczy plik nie przerywa importu
            rec.update(decyzja="blad", opis=f"{type(exc).__name__}: {exc}")
        db.record_seen(batch_id, f.name, f.server_relative_url, f.size, f.modified, rec.get("sha256"), rec["decyzja"], rec.get("opis", ""))
        progress(f"{prefix}: {rec['decyzja']}" + (f" – {rec['opis']}" if rec.get("opis") else ""))
        result.records.append(rec)

    shutil.rmtree(tmp, ignore_errors=True)
    try:
        tmp.parent.rmdir()  # usuwa pusty katalog _tmp
    except OSError:
        pass
    imported = [r for r in result.records if r["decyzja"] == "zaimportowany"]
    result.sets = inspekcja.check_sets([{"plik": r["plik"], "inspekcja": r["inspekcja"]} for r in imported])
    db.finish_batch(batch_id, len(files), len(imported), len(files) - len(imported) - result.errors, result.errors)
    return result


def _download_and_import(f: RemoteFile, download: Downloader, db: Database, tmp: Path, target: Path,
                         batch_id: str, user: str) -> dict[str, Any]:
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
        "sha256": sha, "plik": f.name, "zrodlo": f.server_relative_url, "rozmiar": f.size, "zmodyfikowany": f.modified,
        "landing": str(final), "batch": batch_id, "uzytkownik": user, "typ": info.get("typ"), "arkusz": info.get("arkusz"),
        "kodowanie": info.get("kodowanie"), "separator": info.get("separator"), "kolumny": info.get("kolumny", []),
        "sygnatura": inspekcja.header_signature(info.get("kolumny", [])), "typ_raportu": inspekcja.guess_report_type(f.name),
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
            "typ_raportu": meta["typ_raportu"], "opis": f"{fmt_int(rows)} wierszy, typ {meta['typ_raportu']}"}
