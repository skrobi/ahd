"""Etap 1 (MVP): import plików SAP (RABIT) do bazy – wszystkie pliki, bez znajomości zakresu.

Przykłady (Windows, w katalogu repozytorium):

    python -m ahd.etap1 pobierz  --webdav "<link do folderu SharePoint>" --cel "\\\\serwer\\udzial\\AHD\\00_Global\\RABIT\\Do_importu"
    python -m ahd.etap1 import   --webdav "<link>" --landing C:\\AHD_TEST\\LandingZone
    python -m ahd.etap1 sprawdz  --url "<link do folderu SharePoint>"
    python -m ahd.etap1 lista    --url "<link>"
    python -m ahd.etap1 import   --url "<link>" --landing C:\\AHD_TEST\\LandingZone
    python -m ahd.etap1 import   --folder "\\\\serwer\\udzial\\RABIT" --landing C:\\AHD_TEST\\LandingZone
    python -m ahd.etap1 historia --landing C:\\AHD_TEST\\LandingZone
"""

from __future__ import annotations

import argparse
import fnmatch
import os
import sys
from pathlib import Path
from typing import Callable, Iterable

from ahd.baza import Database, DatabaseError
from ahd.zrodla.folder import FolderSource
from ahd.zrodla.sharepoint import RemoteFile, SharePointClient, SharePointError, build_auth, parse_sharepoint_url
from ahd.zrodla.webdav import SIZE_LIMIT_HINT, unc_from_url

from . import importer


def _use_windows_certificates() -> None:
    # Certyfikaty firmowe są w magazynie Windows; truststore pozwala z nich korzystać.
    try:
        import truststore

        truststore.inject_into_ssl()
    except ImportError:
        pass


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
    """Zwraca (funkcja pobierająca plik, opis źródła, pliki)."""
    folder = unc_from_url(args.webdav) if getattr(args, "webdav", None) else args.folder
    if folder:
        src = FolderSource(folder)
        return src.download, str(folder), src.walk(recursive=args.rekurencyjnie)
    loc = parse_sharepoint_url(args.url)
    client = SharePointClient(loc.site_url, auth=build_auth(args.auth), verify=args.ca_bundle or True)
    host = loc.site_url.split("/", 3)
    return client.download, f"{host[0]}//{host[2]}{loc.folder}", client.walk(loc.folder, recursive=args.rekurencyjnie)


def _db_url(args: argparse.Namespace) -> str:
    if args.baza:
        return args.baza
    if os.environ.get("AHD_BAZA"):
        return os.environ["AHD_BAZA"]
    return "sqlite:///" + (Path(args.landing).resolve() / "ahd_mvp.sqlite").as_posix()


def cmd_sprawdz(args: argparse.Namespace) -> int:
    loc = parse_sharepoint_url(args.url)
    print(f"Witryna: {loc.site_url}")
    print(f"Folder : {loc.folder}")
    client = SharePointClient(loc.site_url, auth=build_auth(args.auth), verify=args.ca_bundle or True)
    print(f"Zalogowano jako: {client.check()}")
    files, folders = client.list_folder(loc.folder)
    print(f"W folderze: {len(files)} plików, {len(folders)} podfolderów")
    return 0


def cmd_lista(args: argparse.Namespace) -> int:
    _dl, desc, files = _source(args)
    print(f"Źródło: {desc}")
    count = 0
    for f in files:
        if _matches(f.name, args.filtr):
            count += 1
            print(f"  {f.modified[:19]:19}  {_human(f.size):>10}  {f.name}")
    print(f"Razem: {count} plików")
    return 0


def cmd_import(args: argparse.Namespace) -> int:
    download, desc, files = _source(args)
    selected = [f for f in files if _matches(f.name, args.filtr)]
    db_url = _db_url(args)
    print(f"Źródło      : {desc}")
    print(f"Landing Zone: {Path(args.landing).resolve()}")
    print(f"Baza        : {db_url}")
    print(f"Plików      : {len(selected)} ({_human(sum(f.size for f in selected))})")
    if args.dry_run:
        for f in selected:
            print(f"  {_human(f.size):>10}  {f.name}")
        return 0

    db = Database(db_url)
    try:
        result = importer.run_import(selected, download, db, Path(args.landing), desc, full_check=args.pelne_sprawdzenie)
    finally:
        db.close()

    for s in result.sets:
        print(f"Zestaw {s['zestaw']}: {s['opis']} ({importer.fmt_int(s['wiersze_razem'])} wierszy)")
    print(f"Import {result.batch_id}: zaimportowane {result.count('zaimportowany')}, "
          f"bez zmian {result.count('pominiety (metadane)')}, duplikaty {result.count('duplikat')}, błędy {result.errors}")
    bad_sets = [s for s in result.sets if s["waga"] == "BLAD"]
    if bad_sets:
        print("Uwaga: części zestawu mają różne kolumny – sprawdź eksport RABIT.", file=sys.stderr)
    return 1 if result.errors or bad_sets else 0


def _same_file(dest: Path, f: RemoteFile) -> bool:
    """Plik w celu ma ten sam rozmiar i czas modyfikacji co w źródle (tolerancja 2 s – FAT/WebDAV)."""
    import datetime as dt

    if not dest.exists():
        return False
    st = dest.stat()
    try:
        src_ts = dt.datetime.fromisoformat(f.modified).timestamp()
    except ValueError:
        return False
    return st.st_size == f.size and abs(st.st_mtime - src_ts) <= 2


def cmd_pobierz(args: argparse.Namespace) -> int:
    download, desc, files = _source(args)
    target = Path(args.cel)
    selected = [f for f in files if _matches(f.name, args.filtr)]
    print(f"Źródło: {desc}")
    print(f"Cel   : {target}")
    counts = {"skopiowany": 0, "bez zmian": 0, "blad": 0}
    for i, f in enumerate(selected, 1):
        dest = target / f.name
        if _same_file(dest, f):
            decision = "bez zmian"
        elif args.dry_run:
            decision = "do skopiowania"
        else:
            try:
                target.mkdir(parents=True, exist_ok=True)
                download(f, dest)
                decision = "skopiowany"
            except OSError as exc:
                decision = "blad"
                too_big = getattr(exc, "winerror", None) == 223 or "0x800700DF" in str(exc) or f.size > 50_000_000
                print(f"[{i}/{len(selected)}] {f.name} ({_human(f.size)}): BŁĄD {exc}" + (f"\n    {SIZE_LIMIT_HINT}" if too_big else ""))
                counts["blad"] += 1
                continue
        counts[decision] = counts.get(decision, 0) + 1
        print(f"[{i}/{len(selected)}] {f.name} ({_human(f.size)}): {decision}")
    print("Wynik: " + ", ".join(f"{k} {v}" for k, v in counts.items() if v or k != "do skopiowania"))
    return 1 if counts["blad"] else 0


def cmd_historia(args: argparse.Namespace) -> int:
    db = Database(_db_url(args))
    try:
        print("Ostatnie importy:")
        for start, user, seen, imp, skip, err, status in db.batches(args.limit):
            print(f"  {str(start)[:19]:19}  {user:15}  widziane {seen or 0:>4}  nowe {imp or 0:>4}  pominięte {skip or 0:>4}  błędy {err or 0:>3}  {status}")
        print("Ostatnio zaimportowane pliki:")
        for when, user, name, rtype, rows, sha in db.history(args.limit):
            print(f"  {str(when)[:19]:19}  {user:15}  {rtype or '':9}  {rows or 0:>9}  {sha[:12]}  {name}")
    finally:
        db.close()
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="python -m ahd.etap1", description="AHD – etap 1: import plików SAP (MVP)")
    sub = p.add_subparsers(dest="cmd", required=True)

    def conn(sp: argparse.ArgumentParser, folder_allowed: bool = True) -> None:
        g = sp.add_mutually_exclusive_group(required=True)
        g.add_argument("--url", help="link do folderu SharePoint (skopiowany z przeglądarki)")
        if folder_allowed:
            g.add_argument("--folder", help="folder, pojedynczy plik lub .zip – pliki pobrane ręcznie z przeglądarki (także dysk sieciowy)")
            g.add_argument("--webdav", help="link do folderu SharePoint – odczyt przez WebDAV (jak „Otwórz w Eksploratorze”)")
        sp.add_argument("--auth", choices=["sso", "ntlm", "none"], default="sso", help="logowanie (domyślnie SSO Windows)")
        sp.add_argument("--ca-bundle", help="plik z certyfikatami CA (gdy nie działa truststore)")
        sp.add_argument("--filtr", action="append", help="wzorzec nazwy, np. *.xlsx (można powtórzyć)")
        sp.add_argument("--rekurencyjnie", action="store_true", help="także podfoldery")

    def dbopt(sp: argparse.ArgumentParser) -> None:
        sp.add_argument("--landing", required=True, help="korzeń Landing Zone, np. C:\\AHD_TEST\\LandingZone")
        sp.add_argument("--baza", help="adres bazy: sqlite:///… lub mssql://SERWER/BAZA (domyślnie plik SQLite w Landing Zone)")

    s = sub.add_parser("sprawdz", help="sprawdź połączenie i logowanie do SharePoint")
    g = s.add_mutually_exclusive_group(required=True)
    g.add_argument("--url", help="link do folderu SharePoint")
    s.add_argument("--auth", choices=["sso", "ntlm", "none"], default="sso")
    s.add_argument("--ca-bundle")
    s.set_defaults(func=cmd_sprawdz)

    s = sub.add_parser("lista", help="pokaż pliki w źródle")
    conn(s)
    s.set_defaults(func=cmd_lista)

    s = sub.add_parser("import", help="zaimportuj nowe pliki do bazy (wszystkie, bez zakresu)")
    conn(s)
    dbopt(s)
    s.add_argument("--pelne-sprawdzenie", action="store_true", help="pobierz i policz hash także plików bez zmian w metadanych")
    s.add_argument("--dry-run", action="store_true", help="tylko pokaż pliki w źródle")
    s.set_defaults(func=cmd_import)

    s = sub.add_parser("pobierz", help="skopiuj pliki ze źródła do folderu na dysku (tylko nowe i zmienione)")
    conn(s)
    s.add_argument("--cel", required=True, help="folder docelowy, np. \\\\serwer\\udzial\\AHD\\00_Global\\RABIT\\Do_importu")
    s.add_argument("--dry-run", action="store_true", help="tylko pokaż, co zostałoby skopiowane")
    s.set_defaults(func=cmd_pobierz)

    s = sub.add_parser("historia", help="pokaż ostatnie importy i pliki w bazie")
    dbopt(s)
    s.add_argument("--limit", type=int, default=20)
    s.set_defaults(func=cmd_historia)
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    _use_windows_certificates()
    try:
        return args.func(args)
    except (SharePointError, DatabaseError, ValueError, FileNotFoundError) as exc:
        print(f"BŁĄD: {exc}", file=sys.stderr)
        return 1
