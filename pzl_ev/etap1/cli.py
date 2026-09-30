"""Etap 1: pobranie plików RABIT przez WebDAV i import do bazy (bez znajomości zakresu).

Przepływ docelowy (Windows, w katalogu repozytorium):

    1. python -m pzl_ev.etap1 pobierz     --webdav "<link do folderu RABIT>" --cel "<PZL-EV>\\00_Global\\RABIT\\Do_importu"
    2. python -m pzl_ev.etap1 import      --folder "<PZL-EV>\\00_Global\\RABIT\\Do_importu" --landing "<PZL-EV>\\01_LandingZone"
    3. python -m pzl_ev.etap1 historia    --landing "<PZL-EV>\\01_LandingZone"

`pobierz` kopiuje tylko pliki nowe i zmienione. `import` rozpoznaje źródło po prefiksie nazwy pliku
(`zrodla_rabit.csv`) i ładuje do bazy tylko pliki o nowej treści (SHA-256).
Plik pobrany ręcznie w przeglądarce (awaryjnie, np. > 50 MB) wystarczy zapisać w `Do_importu`.
"""

from __future__ import annotations

import argparse
import datetime as dt
import fnmatch
import os
import sys
from pathlib import Path
from typing import Callable, Iterable

from pzl_ev.baza import Database, DatabaseError
from pzl_ev.konfiguracja import ConfigError, default_dir, load_sources, match_source
from pzl_ev.zrodla.folder import FolderSource, RemoteFile
from pzl_ev.zrodla.webdav import SIZE_LIMIT_HINT, unc_from_url

from . import importer

WEBCLIENT_LIMIT = 50_000_000


def _human(n: int) -> str:
    size = float(n)
    for unit in ("B", "KB", "MB"):
        if size < 1024:
            return f"{size:.0f} {unit}" if unit == "B" else f"{size:.1f} {unit}"
        size /= 1024
    return f"{size:.1f} GB"


def _matches(name: str, patterns: list[str] | None) -> bool:
    return not patterns or any(fnmatch.fnmatch(name.lower(), p.lower()) for p in patterns)


def _source(args: argparse.Namespace) -> tuple[Callable[[RemoteFile, Path], Path], str, Iterable[RemoteFile]]:
    """Zwraca (funkcja kopiująca plik, opis źródła, pliki)."""
    folder = unc_from_url(args.webdav) if args.webdav else args.folder
    src = FolderSource(folder)
    return src.download, str(folder), src.walk(recursive=args.rekurencyjnie)


def _db_url(args: argparse.Namespace) -> str:
    if args.baza:
        return args.baza
    if os.environ.get("PZL_EV_BAZA"):
        return os.environ["PZL_EV_BAZA"]
    return "sqlite:///" + (Path(args.landing).resolve() / "pzl_ev_mvp.sqlite").as_posix()


def _same_file(dest: Path, f: RemoteFile) -> bool:
    """Plik w celu ma ten sam rozmiar i czas modyfikacji co w źródle (tolerancja 2 s)."""
    if not dest.exists():
        return False
    st = dest.stat()
    try:
        src_ts = dt.datetime.fromisoformat(f.modified).timestamp()
    except ValueError:
        return False
    return st.st_size == f.size and abs(st.st_mtime - src_ts) <= 2


def cmd_lista(args: argparse.Namespace) -> int:
    sources = load_sources(args.konfiguracja)
    _copy, desc, files = _source(args)
    print(f"Źródło: {desc}")
    count = unknown = 0
    for f in files:
        if _matches(f.name, args.filtr):
            count += 1
            src = match_source(f.name, sources)
            unknown += src is None
            print(f"  {f.modified[:19]:19}  {_human(f.size):>10}  {(src.code if src else '— nierozpoznany'):22}  {f.name}")
    print(f"Razem: {count} plików, nierozpoznanych: {unknown}")
    return 0


def cmd_pobierz(args: argparse.Namespace) -> int:
    copy, desc, files = _source(args)
    target = Path(args.cel)
    selected = [f for f in files if _matches(f.name, args.filtr)]
    print(f"Źródło: {desc}")
    print(f"Cel   : {target}")
    counts = {"skopiowany": 0, "bez zmian": 0, "do skopiowania": 0, "blad": 0}
    for i, f in enumerate(selected, 1):
        dest = target / f.name
        prefix = f"[{i}/{len(selected)}] {f.name} ({_human(f.size)})"
        if _same_file(dest, f):
            decision = "bez zmian"
        elif args.dry_run:
            decision = "do skopiowania"
        else:
            try:
                copy(f, dest)
                decision = "skopiowany"
            except OSError as exc:
                counts["blad"] += 1
                hint = f"\n    {SIZE_LIMIT_HINT}" if f.size > WEBCLIENT_LIMIT or "0x800700DF" in str(exc) else ""
                print(f"{prefix}: BŁĄD {exc}{hint}")
                continue
        counts[decision] += 1
        print(f"{prefix}: {decision}")
    print("Wynik: " + ", ".join(f"{k} {v}" for k, v in counts.items() if v or k in ("skopiowany", "bez zmian")))
    return 1 if counts["blad"] else 0


def cmd_import(args: argparse.Namespace) -> int:
    sources = load_sources(args.konfiguracja)
    copy, desc, files = _source(args)
    selected = [f for f in files if _matches(f.name, args.filtr)]
    db_url = _db_url(args)
    print(f"Źródło      : {desc}")
    print(f"Landing Zone: {Path(args.landing).resolve()}")
    print(f"Baza        : {db_url}")
    print(f"Plików      : {len(selected)} ({_human(sum(f.size for f in selected))})")
    if args.dry_run:
        for f in selected:
            src = match_source(f.name, sources)
            print(f"  {_human(f.size):>10}  {(src.code if src else '— nierozpoznany'):22}  {f.name}")
        return 0

    db = Database(db_url)
    try:
        result = importer.run_import(selected, copy, sources, db, Path(args.landing), desc,
                                     full_check=args.pelne_sprawdzenie)
    finally:
        db.close()

    print(f"Import {result.batch_id}: zaimportowane {result.count('zaimportowany')}, "
          f"bez zmian {result.count('pominiety (metadane)')}, duplikaty {result.count('duplikat')}, "
          f"nierozpoznane {result.count('nierozpoznany')}, błędy {result.errors}")
    if result.count("nierozpoznany"):
        print("Pliki nierozpoznane nie zostały zaimportowane – dopisz ich prefiks do zrodla_rabit.csv.", file=sys.stderr)
    return 1 if result.errors else 0


def cmd_historia(args: argparse.Namespace) -> int:
    db = Database(_db_url(args))
    try:
        print("Ostatnie importy:")
        for start, user, seen, imp, skip, err, status in db.batches(args.limit):
            print(f"  {str(start)[:19]:19}  {user:15}  widziane {seen or 0:>4}  nowe {imp or 0:>4}  pominięte {skip or 0:>4}  błędy {err or 0:>3}  {status}")
        print("Ostatnio zaimportowane pliki:")
        for when, user, name, code, rows, sha in db.history(args.limit):
            print(f"  {str(when)[:19]:19}  {user:15}  {code or '':22}  {rows or 0:>9}  {sha[:12]}  {name}")
    finally:
        db.close()
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="python -m pzl_ev.etap1", description="PZL-EV – etap 1: pobranie i import plików RABIT")
    sub = p.add_subparsers(dest="cmd", required=True)

    def source(sp: argparse.ArgumentParser) -> None:
        g = sp.add_mutually_exclusive_group(required=True)
        g.add_argument("--webdav", help="link do folderu RABIT na SharePoint (albo ścieżka \\\\host@SSL\\DavWWWRoot\\…)")
        g.add_argument("--folder", help="folder lub pojedynczy plik na dysku, np. 00_Global\\RABIT\\Do_importu")
        sp.add_argument("--filtr", action="append", help="wzorzec nazwy, np. *.xlsx (można powtórzyć)")
        sp.add_argument("--rekurencyjnie", action="store_true", help="także podfoldery")

    def config(sp: argparse.ArgumentParser) -> None:
        sp.add_argument("--konfiguracja", type=Path, default=default_dir(),
                        help="katalog z zrodla_rabit.csv (domyślnie ./konfiguracja lub PZL_EV_KONFIGURACJA)")

    def database(sp: argparse.ArgumentParser) -> None:
        sp.add_argument("--landing", required=True, help="korzeń Landing Zone, np. \\\\serwer\\udzial\\PZL-EV\\01_LandingZone")
        sp.add_argument("--baza", help="adres bazy: sqlite:///… lub mssql://SERWER/BAZA (domyślnie plik SQLite w Landing Zone)")

    s = sub.add_parser("pobierz", help="skopiuj pliki RABIT na dysk (tylko nowe i zmienione)")
    source(s)
    s.add_argument("--cel", required=True, help="folder docelowy, np. \\\\serwer\\udzial\\PZL-EV\\00_Global\\RABIT\\Do_importu")
    s.add_argument("--dry-run", action="store_true", help="tylko pokaż, co zostałoby skopiowane")
    s.set_defaults(func=cmd_pobierz)

    s = sub.add_parser("import", help="zaimportuj rozpoznane pliki o nowej treści do bazy")
    source(s)
    config(s)
    database(s)
    s.add_argument("--pelne-sprawdzenie", action="store_true", help="policz hash także plików bez zmian w metadanych")
    s.add_argument("--dry-run", action="store_true", help="tylko pokaż pliki w źródle")
    s.set_defaults(func=cmd_import)

    s = sub.add_parser("historia", help="pokaż ostatnie importy i pliki w bazie")
    database(s)
    s.add_argument("--limit", type=int, default=20)
    s.set_defaults(func=cmd_historia)

    s = sub.add_parser("lista", help="pokaż pliki w źródle i rozpoznane źródło")
    source(s)
    config(s)
    s.set_defaults(func=cmd_lista)
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return args.func(args)
    except (ConfigError, DatabaseError, ValueError, FileNotFoundError) as exc:
        print(f"BŁĄD: {exc}", file=sys.stderr)
        return 1
