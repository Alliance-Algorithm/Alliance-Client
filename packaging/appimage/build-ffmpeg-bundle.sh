#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

DOCKER="${DOCKER:-docker}"
FFMPEG_VERSION="${FFMPEG_VERSION:-8.1.2}"
IMAGE_TAG="${IMAGE_TAG:-alliance-ffmpeg-bundle:ubuntu2204-${FFMPEG_VERSION}}"
OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/packaging/vendor/ffmpeg/linux-x64-ubuntu2204}"
GLIBC_FLOOR="${GLIBC_FLOOR:-2.35}"
DOCKERFILE_PATH="$SCRIPT_DIR/docker/ffmpeg-bundle.ubuntu2204.Dockerfile"

write_output_readme() {
  cat > "$OUTPUT_DIR/README.md" <<EOF
# Ubuntu 22.04 FFmpeg Bundle

This directory is the formal release-time FFmpeg runtime bundle for Alliance.VideoWorker.

Build baseline:

- Ubuntu 22.04
- glibc $GLIBC_FLOOR
- FFmpeg $FFMPEG_VERSION

This bundle replaces the older Arch Linux based linux-x64 bundle for official AppImage builds.

Regenerate it with:

\`bash packaging/appimage/build-ffmpeg-bundle.sh\`

The resulting libraries are validated to ensure:

- \`libavcodec.so.62\`, \`libavutil.so.60\`, and \`libswscale.so.9\` exist
- the highest required \`GLIBC_*\` version does not exceed \`GLIBC_$GLIBC_FLOOR\`
- heavyweight dependencies such as \`libva\`, \`libvpl\`, \`glib\`, \`cairo\`, \`rsvg\`, \`x264\`, \`x265\`, and \`jxl\` are not pulled into the main decode path
EOF
}

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

max_glibc_version() {
  local lib_path="$1"
  readelf --version-info "$lib_path" \
    | grep -o 'GLIBC_[0-9.]*' \
    | sed 's/^GLIBC_//' \
    | sort -Vu \
    | tail -n 1
}

assert_glibc_floor() {
  local lib_path="$1"
  local max_version
  max_version="$(max_glibc_version "$lib_path")"

  if [[ -z "$max_version" ]]; then
    echo "Unable to determine GLIBC floor for $lib_path" >&2
    exit 1
  fi

  if [[ "$(printf '%s\n' "$max_version" "$GLIBC_FLOOR" | sort -V | tail -n 1)" != "$GLIBC_FLOOR" ]]; then
    echo "GLIBC floor check failed for $(basename "$lib_path"): got GLIBC_$max_version, expected <= GLIBC_$GLIBC_FLOOR" >&2
    exit 1
  fi
}

assert_dependency_absent() {
  local lib_path="$1"
  local bad_name="$2"

  if LD_LIBRARY_PATH="$OUTPUT_DIR" ldd "$lib_path" | grep -q "$bad_name"; then
    echo "Unexpected dependency $bad_name found in $(basename "$lib_path")" >&2
    exit 1
  fi
}

assert_library_absent() {
  local pattern="$1"
  if compgen -G "$OUTPUT_DIR/$pattern" >/dev/null; then
    echo "Unexpected library matched in output bundle: $pattern" >&2
    exit 1
  fi
}

require_command readelf
require_command ldd
DOCKER_BIN="$(resolve_executable "$DOCKER")"

mkdir -p "$OUTPUT_DIR"

"$DOCKER_BIN" build \
  --build-arg FFMPEG_VERSION="$FFMPEG_VERSION" \
  -t "$IMAGE_TAG" \
  -f "$DOCKERFILE_PATH" \
  "$SCRIPT_DIR/docker"

"$DOCKER_BIN" run --rm \
  -v "$OUTPUT_DIR:/out" \
  "$IMAGE_TAG" \
  bash -lc 'find /out -mindepth 1 -delete'

"$DOCKER_BIN" run --rm \
  -u "$(id -u):$(id -g)" \
  -v "$OUTPUT_DIR:/out" \
  "$IMAGE_TAG" \
  bash -lc 'cp -a /opt/ffmpeg-bundle/lib/libavcodec.so* /out/ && cp -a /opt/ffmpeg-bundle/lib/libavutil.so* /out/ && cp -a /opt/ffmpeg-bundle/lib/libswscale.so* /out/'

write_output_readme

for required_lib in libavcodec.so.62 libavutil.so.60 libswscale.so.9; do
  if [[ ! -e "$OUTPUT_DIR/$required_lib" ]]; then
    echo "Required FFmpeg library missing from bundle: $required_lib" >&2
    exit 1
  fi
done

for lib_path in \
  "$OUTPUT_DIR/libavcodec.so.62" \
  "$OUTPUT_DIR/libavutil.so.60" \
  "$OUTPUT_DIR/libswscale.so.9"; do
  assert_glibc_floor "$lib_path"
done

for pattern in \
  'libavdevice.so*' \
  'libavfilter.so*' \
  'libavformat.so*' \
  'libswresample.so*'; do
  assert_library_absent "$pattern"
done

for bad_name in \
  libva.so.2 \
  libvpl.so.2 \
  libglib-2.0.so.0 \
  libcairo.so.2 \
  librsvg-2.so.2 \
  libx264.so.165 \
  libx265.so.216 \
  libjxl.so.0.11; do
  for lib_path in \
    "$OUTPUT_DIR/libavcodec.so.62" \
    "$OUTPUT_DIR/libavutil.so.60" \
    "$OUTPUT_DIR/libswscale.so.9"; do
    assert_dependency_absent "$lib_path" "$bad_name"
  done
done

echo "Ubuntu 22.04 FFmpeg bundle created at $OUTPUT_DIR"
