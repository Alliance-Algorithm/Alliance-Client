#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

CONFIGURATION="${CONFIGURATION:-Release}"
RID="${RID:-linux-x64}"
APPIMAGETOOL="${APPIMAGETOOL:-appimagetool}"
APPIMAGE_RUNTIME_FILE="${APPIMAGE_RUNTIME_FILE:-}"
APPIMAGE_FILENAME="${APPIMAGE_FILENAME:-Alliance-Client-${RID}.AppImage}"
FFMPEG_BUNDLE_DIR="${FFMPEG_BUNDLE_DIR:-$REPO_ROOT/packaging/vendor/ffmpeg/linux-x64-ubuntu2204}"
OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/artifacts/appimage}"

if [[ "$RID" != "linux-x64" ]]; then
  echo "This script currently supports only RID=linux-x64." >&2
  exit 1
fi

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

resolve_executable() {
  local candidate="$1"
  if [[ "$candidate" == */* ]]; then
    if [[ ! -x "$candidate" ]]; then
      echo "Executable not found: $candidate" >&2
      exit 1
    fi
    printf '%s\n' "$candidate"
    return
  fi

  if ! command -v "$candidate" >/dev/null 2>&1; then
    echo "Executable not found in PATH: $candidate" >&2
    exit 1
  fi

  command -v "$candidate"
}

require_file() {
  if [[ ! -e "$1" ]]; then
    echo "Required file missing: $1" >&2
    exit 1
  fi
}

require_command dotnet
require_command ldd
require_command readelf
APPIMAGETOOL_BIN="$(resolve_executable "$APPIMAGETOOL")"

APPIMAGETOOL_RUNTIME_ARGS=()
if [[ -n "$APPIMAGE_RUNTIME_FILE" ]]; then
  require_file "$APPIMAGE_RUNTIME_FILE"
  APPIMAGETOOL_RUNTIME_ARGS=(--runtime-file "$APPIMAGE_RUNTIME_FILE")
fi

for required in libavcodec.so.62 libavutil.so.60 libswscale.so.9 ffmpeg; do
  require_file "$FFMPEG_BUNDLE_DIR/$required"
done

BUILD_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/alliance-appimage.XXXXXX")"
trap 'echo "Build workspace: $BUILD_ROOT"' EXIT

CLIENT_PUBLISH_DIR="$BUILD_ROOT/publish/client"
APPDIR="$BUILD_ROOT/AppDir"
APPDIR_ROOT="$APPDIR/usr/lib/alliance-client"
APPDIR_WORKER_FFMPEG="$APPDIR_ROOT/worker/ffmpeg"
OUTPUT_PATH="$OUTPUT_DIR/$APPIMAGE_FILENAME"

mkdir -p "$CLIENT_PUBLISH_DIR" "$APPDIR_ROOT" "$APPDIR_WORKER_FFMPEG" "$OUTPUT_DIR"

dotnet publish "$REPO_ROOT/src/Alliance.Client/Alliance.Client.csproj" \
  -c "$CONFIGURATION" \
  -r "$RID" \
  --self-contained true \
  -o "$CLIENT_PUBLISH_DIR"

require_file "$CLIENT_PUBLISH_DIR/Alliance.Client"
require_file "$CLIENT_PUBLISH_DIR/appsettings.json"
require_file "$CLIENT_PUBLISH_DIR/worker/Alliance.VideoWorker"

cp -a "$CLIENT_PUBLISH_DIR/." "$APPDIR_ROOT/"
cp -aL "$FFMPEG_BUNDLE_DIR/." "$APPDIR_WORKER_FFMPEG/"

is_system_dependency() {
  local name="$1"
  case "$name" in
    linux-vdso.so.1|ld-linux*.so*|libc.so.*|libm.so.*|libpthread.so.*|librt.so.*|libdl.so.*|libresolv.so.*|libutil.so.*|libnsl.so.*|libcrypt.so.*)
      return 0
      ;;
    libX11.so.*|libXext.so.*|libXfixes.so.*|libxcb.so.*|libXau.so.*|libXdmcp.so.*|libbsd.so.*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

collect_elf_dependency_closure() {
  local target_dir="$1"
  local scan_pattern="${2:-*.so*}"

  local -a queue=()
  local -A seen=()

  while IFS= read -r -d '' file; do
    queue+=("$file")
  done < <(find "$target_dir" -maxdepth 1 -type f \( -name "$scan_pattern" \) -print0 | sort -z)

  local index=0
  while (( index < ${#queue[@]} )); do
    local current="${queue[index]}"
    ((index += 1))

    while IFS= read -r line; do
      if [[ "$line" == *"=> not found"* ]]; then
        local soname="${line%%=> not found*}"
        soname="${soname##* }"
        if ! is_system_dependency "$soname"; then
          echo "Unresolved dependency while scanning $current: $line" >&2
          exit 1
        fi
        continue
      fi

      if [[ "$line" =~ '=> '[[:space:]]*(/[^[:space:]]+) ]]; then
        local source_path="${BASH_REMATCH[1]}"
        local base_name
        base_name="$(basename "$source_path")"

        if [[ "$source_path" == "$target_dir"/* ]]; then
          seen[$base_name]=1
          continue
        fi

        if is_system_dependency "$base_name"; then
          continue
        fi

        if [[ -n "${seen[$base_name]:-}" ]]; then
          continue
        fi

        local destination_path="$target_dir/$base_name"
        if [[ ! -e "$destination_path" ]]; then
          echo "Bundling dependency: $base_name" >&2
          cp -aL "$source_path" "$destination_path"
          chmod a+x "$destination_path"
        fi

        seen[$base_name]=1
        queue+=("$destination_path")
      fi
    done < <(LD_LIBRARY_PATH="$target_dir${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" ldd "$current")
  done
}

verify_elf_bundle_closure() {
  local target_dir="$1"
  local scan_pattern="${2:-*.so*}"

  while IFS= read -r -d '' file; do
    while IFS= read -r line; do
      if [[ "$line" =~ Shared[[:space:]]library:[[:space:]]\[(.+)\] ]]; then
        local needed_name="${BASH_REMATCH[1]}"
        if [[ -e "$target_dir/$needed_name" ]]; then
          continue
        fi

        if is_system_dependency "$needed_name"; then
          continue
        fi

        echo "Incomplete bundle: $(basename "$file") requires $needed_name but it was not bundled." >&2
        exit 1
      fi
    done < <(readelf -d "$file")
  done < <(find "$target_dir" -maxdepth 1 -type f \( -name "$scan_pattern" \) -print0 | sort -z)
}

collect_elf_dependency_closure "$APPDIR_WORKER_FFMPEG" "*"
verify_elf_bundle_closure "$APPDIR_WORKER_FFMPEG" "*"

mkdir -p \
  "$APPDIR/usr/share/applications" \
  "$APPDIR/usr/share/icons/hicolor/scalable/apps"

install -m 755 "$SCRIPT_DIR/AppRun" "$APPDIR/AppRun"
install -m 644 "$SCRIPT_DIR/alliance-client.desktop" "$APPDIR/alliance-client.desktop"
install -m 644 "$SCRIPT_DIR/alliance-client.desktop" "$APPDIR/usr/share/applications/alliance-client.desktop"
install -m 644 "$SCRIPT_DIR/alliance-client.svg" "$APPDIR/alliance-client.svg"
install -m 644 "$SCRIPT_DIR/alliance-client.svg" "$APPDIR/.DirIcon"
install -m 644 "$SCRIPT_DIR/alliance-client.svg" "$APPDIR/usr/share/icons/hicolor/scalable/apps/alliance-client.svg"

export ARCH=x86_64
APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL_BIN" \
  "${APPIMAGETOOL_RUNTIME_ARGS[@]}" \
  "$APPDIR" "$OUTPUT_PATH"

require_file "$OUTPUT_PATH"
echo "AppImage created at $OUTPUT_PATH"
