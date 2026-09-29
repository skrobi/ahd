"""Etap 1 (MVP): pobranie plików SAP z SharePoint lub folderu do Landing Zone.

Przykłady (Windows, w katalogu repozytorium):

    python -m ahd.etap1 sprawdz --url "<link do folderu SharePoint>"
    python -m ahd.etap1 lista   --url "<link>"
    python -m ahd.etap1 pobierz --url "<link>" --cel C:\\AHD_TEST\\LandingZone --zakres F16
    python -m ahd.etap1 pobierz --folder "\\\\serwer\\udzial\\SAP" --cel ... --zakres F16
"""

from __future__ import annotations

import argparse
import datetime as dt
import fnmatch
import sys
from pathlib import Path
from typing import Any, Iterable

from ahd.zrodla.folder import FolderSource
from ahd.zrodla.sharepoint import RemoteFile, SharePointClient, SharePointError, build_auth, parse_sharepoint_url

from . import inspekcja, landing


def _use_windows_certificates() -> None:
    # Certyfikaty firmowe są w magazynie Windows; truststore pozwala z nich korzystać.
    try:
        import truststore

        truststore.inject_into_ssl()
    except ImportError:
        pass


def _human(n: int) -> str:
    size = float(n)
    for unit in ("B", "KB", "MB", "GB"):
        if size < 1024 or unit == "GB":
            return f"{size:.0f} {unit}" if unit == "B" else f"{size:.1f} {unit}"
        size /= 1024
    return f"{n} B"


def _matches(name: str, patterns: list[str] | None) -> bool:
    return not patterns or any(fnmatch.fnmatch(name.lower(), p.lower()) for p in patterns)


def _source(args: argparse.Namespace) -> tuple[Any, str, Iterable[RemoteFile]]:
    """Zwraca (źródło z metodą download, opis, iterator plików)."""
    if args.folder:
        src = FolderSource(args.folder)
        return src, str(Path(args.folder)), src.walk(recursive=args.rekurencyjnie)
    loc = parse_sharepoint_url(args.url)
    client = SharePointClient(
        loc.site_url,
        auth=build_auth(args.auth),
        verify=args.ca_bundle or True,
    )
    return client, f"{loc.site_url} :: {loc.folder}", client.walk(loc.folder, recursive=args.rekurencyjnie)


def cmd_sprawdz(args: argparse.Namespace) -> int:
    loc = parse_sharepoint_url(args.url)
    print(f"Witryna: {loc.site_url}")
    print(f"Folder : {loc.folder}")
    client = SharePointClient(loc.site_url, auth=build_auth(args.auth), verify=args.ca_bundle or True)
    user = client.check()
    print(f"Zalogowano jako: {user}")
    files, folders = client.list_folder(loc.folder)
    print(f"W folderze: {len(files)} plików, {len(folders)} podfolderów")
    return 0


def cmd_lista(args: argparse.Namespace) -> int:
    _src, desc, files = _source(args)
    print(f"Źródło: {desc}")
    count = 0
    for f in files:
        if _matches(f.name, args.filtr):
            count += 1
            print(f"  {f.modified[:19]:19}  {_human(f.size):>10}  {f.name}")
    print(f"Razem: {count} plików")
    return 0


def _unique_dest(directory: Path, name: str) -> Path:
    dest = directory / name
    i = 2
    while dest.exists():
        dest = directory / f"{Path(name).stem} ({i}){Path(name).suffix}"
        i += 1
    return dest


def cmd_pobierz(args: argparse.Namespace) -> int:
    today = dt.date.today()
    okres = args.okres or today.strftime("%Y-%m")
    tydzien = None if args.zamkniecie else (args.tydzien or today.isocalendar()[1])
    rid = landing.run_id(args.zakres, okres, tydzien)
    target = landing.run_dir(Path(args.cel), args.zakres, okres, rid)

    if (target / "manifest.json").exists() and not args.nadpisz:
        print(f"Przebieg {rid} ma już pobrane pliki: {target}\nUżyj --nadpisz, aby pobrać ponownie.", file=sys.stderr)
        return 2

    src, desc, files = _source(args)
    selected = [f for f in files if _matches(f.name, args.filtr)]
    print(f"Przebieg : {rid}")
    print(f"Źródło   : {desc}")
    print(f"Cel      : {target}")
    print(f"Plików   : {len(selected)}")
    if args.dry_run or not selected:
        for f in selected:
            print(f"  {_human(f.size):>10}  {f.name}")
        return 0

    target.mkdir(parents=True, exist_ok=True)
    known = landing.previous_hashes(Path(args.cel), args.zakres, exclude=target)
    manifest = landing.new_manifest(rid, args.zakres, okres, desc)
    errors = 0
    for i, f in enumerate(selected, 1):
        print(f"[{i}/{len(selected)}] {f.name} ({_human(f.size)}) … ", end="", flush=True)
        rec: dict[str, Any] = {"plik": f.name, "zrodlo": f.server_relative_url, "rozmiar": f.size, "zmodyfikowany": f.modified}
        try:
            dest = src.download(f, _unique_dest(target, f.name))
            rec["plik"] = dest.name
            rec["sha256"] = landing.sha256_file(dest)
            rec["status"] = landing.change_status(dest.name, rec["sha256"], known)
            rec["inspekcja"] = inspekcja.inspect_file(dest)
            rows = rec["inspekcja"].get("wiersze")
            print(f"{rec['status']}" + (f", {rows:,} wierszy".replace(",", " ") if rows is not None else ""))
        except (SharePointError, OSError) as exc:
            errors += 1
            rec.update(status="blad", blad=str(exc), inspekcja={})
            print(f"BŁĄD: {exc}")
        manifest["pliki"].append(rec)

    manifest["zestawy"] = inspekcja.check_sets([r for r in manifest["pliki"] if r["status"] != "blad"])
    manifest["koniec"] = dt.datetime.now().astimezone().isoformat(timespec="seconds")
    path = landing.write_manifest(target, manifest)

    for s in manifest["zestawy"]:
        print(f"Zestaw {s['zestaw']}: {s['opis']} ({s['wiersze_razem']:,} wierszy)".replace(",", " "))
    bad_sets = [s for s in manifest["zestawy"] if s["waga"] == "BLAD"]
    print(f"Manifest: {path}")
    if errors or bad_sets:
        print(f"Wynik: {errors} błędów pobierania, {len(bad_sets)} niespójnych zestawów.", file=sys.stderr)
        return 1
    print("Wynik: OK")
    return 0


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="python -m ahd.etap1", description="AHD – etap 1: pobranie plików SAP (MVP)")
    sub = p.add_subparsers(dest="cmd", required=True)

    def conn(sp: argparse.ArgumentParser, folder_allowed: bool = True) -> None:
        g = sp.add_mutually_exclusive_group(required=True)
        g.add_argument("--url", help="link do folderu SharePoint (skopiowany z przeglądarki)")
        if folder_allowed:
            g.add_argument("--folder", help="folder lokalny / sieciowy / synchronizowany OneDrive")
        sp.add_argument("--auth", choices=["sso", "ntlm", "none"], default="sso", help="logowanie (domyślnie SSO Windows)")
        sp.add_argument("--ca-bundle", help="plik z certyfikatami CA (gdy nie działa truststore)")

    s = sub.add_parser("sprawdz", help="sprawdź połączenie i logowanie do SharePoint")
    conn(s, folder_allowed=False)
    s.set_defaults(func=cmd_sprawdz)

    s = sub.add_parser("lista", help="pokaż pliki w źródle")
    conn(s)
    s.add_argument("--filtr", action="append", help="wzorzec nazwy, np. *.xlsx (można powtórzyć)")
    s.add_argument("--rekurencyjnie", action="store_true", help="także podfoldery")
    s.set_defaults(func=cmd_lista)

    s = sub.add_parser("pobierz", help="pobierz pliki do Landing Zone")
    conn(s)
    s.add_argument("--cel", required=True, help="korzeń Landing Zone, np. C:\\AHD_TEST\\LandingZone")
    s.add_argument("--zakres", required=True, help="kod zakresu, np. F16")
    s.add_argument("--okres", help="RRRR-MM (domyślnie bieżący miesiąc)")
    s.add_argument("--tydzien", type=int, help="numer tygodnia (domyślnie bieżący tydzień ISO)")
    s.add_argument("--zamkniecie", action="store_true", help="przebieg zamknięcia miesiąca zamiast tygodniowego")
    s.add_argument("--filtr", action="append", help="wzorzec nazwy, np. *.xlsx (można powtórzyć)")
    s.add_argument("--rekurencyjnie", action="store_true", help="także podfoldery")
    s.add_argument("--nadpisz", action="store_true", help="pobierz ponownie, jeśli przebieg ma już pliki")
    s.add_argument("--dry-run", action="store_true", help="tylko pokaż, co zostałoby pobrane")
    s.set_defaults(func=cmd_pobierz)
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    _use_windows_certificates()
    try:
        return args.func(args)
    except (SharePointError, ValueError, FileNotFoundError) as exc:
        print(f"BŁĄD: {exc}", file=sys.stderr)
        return 1
