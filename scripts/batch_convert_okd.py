"""Batch conversion of OKD files to MIDI using OKDPlayer.exe.

This script discovers OKD files stored under a given root directory (for example,
``D:\\新しいフォルダー (4)\\Song``) and runs the OKDPlayer executable against each of
them in parallel so they are converted into MIDI files next to the source file.

Usage example::

    python batch_convert_okd.py \
        --root "D:\\新しいフォルダー (4)\\Song" \
        --exe "C:\\path\\to\\OKDPlayer.exe"

By default the script searches for files named ``1006.*`` in any nested
sub-directory and writes the converted result as ``<original>.midi`` (for example
``1006.1.midi``) to match the user's folder structure. Both the search pattern
and the degree of parallelism are configurable through command line options.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import logging
import os
import subprocess
import sys
from functools import partial
from pathlib import Path
from typing import Iterable, List, Sequence


def parse_args(argv: Sequence[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Convert OKD files to MIDI by invoking OKDPlayer.exe in parallel."
    )
    parser.add_argument(
        "--root",
        type=Path,
        required=True,
        help="Root directory that contains the OKD files (e.g. D:/新しいフォルダー (4)/Song).",
    )
    parser.add_argument(
        "--exe",
        type=Path,
        required=True,
        help="Path to OKDPlayer.exe (or the compiled converter executable).",
    )
    parser.add_argument(
        "--pattern",
        default="1006.*",
        help=(
            "Glob pattern used to locate OKD files relative to the root directory. "
            "Defaults to '1006.*' to follow the repository's folder layout."
        ),
    )
    parser.add_argument(
        "--workers",
        type=int,
        default=os.cpu_count() or 4,
        help="Number of parallel worker processes to spawn (default: CPU count).",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show the commands that would run without executing them.",
    )
    parser.add_argument(
        "--skip-existing",
        action="store_true",
        help="Skip conversion when the target .midi file already exists.",
    )
    return parser.parse_args(argv)


def discover_okd_files(root: Path, pattern: str) -> List[Path]:
    """Return a sorted list of OKD file paths matching *pattern* under *root*."""
    if not root.exists():
        raise FileNotFoundError(f"Root directory not found: {root}")

    logging.debug("Scanning %s for pattern %s", root, pattern)
    matches = [path for path in root.glob(f"**/{pattern}") if path.is_file()]
    matches.sort()
    return matches


def build_command(exe: Path, okd_path: Path, midi_path: Path) -> List[str]:
    return [
        str(exe),
        "--input-okd-file",
        str(okd_path),
        "--midi-output",
        str(midi_path),
    ]


def convert_single(
    exe: Path,
    okd_path: Path,
    skip_existing: bool = False,
    dry_run: bool = False,
) -> tuple[Path, bool, str]:
    midi_path = Path(f"{okd_path}.midi")
    if skip_existing and midi_path.exists():
        logging.info("Skipping %s because %s already exists", okd_path, midi_path)
        return okd_path, True, "Skipped"

    cmd = build_command(exe, okd_path, midi_path)
    logging.debug("Running: %s", " ".join(cmd))

    if dry_run:
        return okd_path, True, "Dry run"

    try:
        completed = subprocess.run(cmd, check=True, capture_output=True, text=True)
        if completed.stdout:
            logging.debug("%s output:\n%s", okd_path, completed.stdout)
        if completed.stderr:
            logging.debug("%s errors:\n%s", okd_path, completed.stderr)
    except subprocess.CalledProcessError as exc:
        logging.error(
            "Failed to convert %s (exit code %s): %s",
            okd_path,
            exc.returncode,
            exc.stderr or exc.stdout or "",
        )
        return okd_path, False, exc.stderr or exc.stdout or str(exc)

    return okd_path, True, "Converted"


def run_parallel(
    exe: Path,
    okd_files: Iterable[Path],
    workers: int,
    skip_existing: bool,
    dry_run: bool,
) -> None:
    okd_files = list(okd_files)
    total = len(okd_files)
    logging.info("Found %d OKD file(s) to convert.", total)
    if total == 0:
        return

    worker = partial(
        convert_single,
        exe,
        skip_existing=skip_existing,
        dry_run=dry_run,
    )

    completed = 0
    successes = 0
    failures = 0

    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, workers)) as pool:
        for path, ok, status in pool.map(worker, okd_files):
            completed += 1
            progress = f"({completed}/{total}, {completed / total:.1%})"
            if ok:
                successes += 1
                logging.info("%s ✔ %s", progress, path)
            else:
                failures += 1
                logging.warning("%s ✖ %s (%s)", progress, path, status)

    logging.info(
        "Finished conversion: %d succeeded, %d failed, %d total.",
        successes,
        failures,
        total,
    )


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv or sys.argv[1:])

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s - %(levelname)s - %(message)s",
    )

    exe = args.exe.resolve()
    if not exe.exists():
        logging.error("Executable not found: %s", exe)
        return 1

    try:
        okd_files = discover_okd_files(args.root, args.pattern)
    except FileNotFoundError as exc:
        logging.error(str(exc))
        return 1

    run_parallel(exe, okd_files, args.workers, args.skip_existing, args.dry_run)
    return 0


if __name__ == "__main__":
    sys.exit(main())
