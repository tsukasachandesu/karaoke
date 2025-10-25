#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<USAGE
Usage: $(basename "$0") [options]

Options:
  --archive-dir <path>  Directory where a ZIP archive of the published build will be stored.
                        Defaults to <repo>/release. Relative paths are resolved from the repo root.
  --no-archive          Skip creating a ZIP archive of the published build.
  -h, --help            Show this message.
USAGE
}

ARCHIVE_DIR=""
CREATE_ARCHIVE=true

while [[ $# -gt 0 ]]; do
  case "$1" in
    --archive-dir)
      if [[ $# -lt 2 ]]; then
        echo "Error: --archive-dir requires a path" >&2
        usage
        exit 1
      fi
      ARCHIVE_DIR="$2"
      shift 2
      ;;
    --no-archive)
      CREATE_ARCHIVE=false
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage
      exit 1
      ;;
  esac
done

# Resolve repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOTNET_DIR="$REPO_ROOT/.dotnet"
DOTNET_BIN="$DOTNET_DIR/dotnet"
PUBLISH_DIR="$REPO_ROOT/publish/win-x64"

if [[ -n "$ARCHIVE_DIR" ]]; then
  if [[ "$ARCHIVE_DIR" != /* ]]; then
    ARCHIVE_DIR="$REPO_ROOT/$ARCHIVE_DIR"
  fi
else
  ARCHIVE_DIR="$REPO_ROOT/release"
fi
ARCHIVE_NAME="OKDPlayer-win-x64.zip"
ARCHIVE_PATH="$ARCHIVE_DIR/$ARCHIVE_NAME"

need_install_dotnet=false

if command -v dotnet >/dev/null 2>&1; then
  DOTNET_CMD="$(command -v dotnet)"
else
  need_install_dotnet=true
fi

if [ "$need_install_dotnet" = true ]; then
  mkdir -p "$DOTNET_DIR"
  DOTNET_INSTALL_SCRIPT="$DOTNET_DIR/dotnet-install.sh"

  if [ ! -f "$DOTNET_INSTALL_SCRIPT" ]; then
    echo "Downloading dotnet-install.sh..." >&2
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$DOTNET_INSTALL_SCRIPT"
  fi

  chmod +x "$DOTNET_INSTALL_SCRIPT"
  echo "Installing .NET SDK locally..." >&2
  "$DOTNET_INSTALL_SCRIPT" --channel 8.0 --install-dir "$DOTNET_DIR" --no-path

  export DOTNET_ROOT="$DOTNET_DIR"
  export PATH="$DOTNET_ROOT:$PATH"
  DOTNET_CMD="$DOTNET_BIN"
fi

mkdir -p "$PUBLISH_DIR"

"$DOTNET_CMD" publish "$REPO_ROOT/OKDPlayer.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$PUBLISH_DIR"

cat <<INFO

Windows x64 release build created at: $PUBLISH_DIR
The folder contains OKDPlayer.exe and all dependencies.
INFO

if [ "$CREATE_ARCHIVE" = true ]; then
  mkdir -p "$ARCHIVE_DIR"
  if command -v zip >/dev/null 2>&1; then
    rm -f "$ARCHIVE_PATH"
    (cd "$PUBLISH_DIR" && zip -qr "$ARCHIVE_PATH" .)
    echo "Release archive created at: $ARCHIVE_PATH"
  else
    echo "zip command not found; skipping archive creation." >&2
  fi
fi
