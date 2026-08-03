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

This directory is the release-time FFmpeg runtime bundle for Alliance.VideoWorker
and Alliance.Client screen recording.

Build baseline:

- Ubuntu 22.04
- glibc $GLIBC_FLOOR
- FFmpeg $FFMPEG_VERSION

Regenerate it with:

\`bash packaging/appimage/build-ffmpeg-bundle.sh\`

The resulting bundle is validated to ensure:

- \`ffmpeg\` exists and is a valid ELF executable
- \`libavcodec.so.62\`, \`libavutil.so.60\`, and \`libswscale.so.9\` exist
- \`libx264.so\` is present
- the highest required \`GLIBC_*\` version does not exceed \`GLIBC_$GLIBC_FLOOR\`
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

require_command readelf
require_command ldd
require_command file
require_command patchelf
DOCKER_BIN="$(resolve_executable "$DOCKER")"

mkdir -p "$OUTPUT_DIR"

"$DOCKER_BIN" build \
  --build-arg FFMPEG_VERSION="$FFMPEG_VERSION" \
  -t "$IMAGE_TAG" \
  -f "$DOCKERFILE_PATH" \
  "$SCRIPT_DIR/docker"

"$DOCKER_BIN" run --rm \
  -u "$(id -u):$(id -g)" \
  -v "$OUTPUT_DIR:/out" \
  "$IMAGE_TAG" \
  bash -lc 'find /out -mindepth 1 -delete'

"$DOCKER_BIN" run --rm \
  -u "$(id -u):$(id -g)" \
  -v "$OUTPUT_DIR:/out" \
  "$IMAGE_TAG" \
  bash -lc 'cp -a /opt/ffmpeg-bundle/lib/*.so* /out/ && cp -a /opt/ffmpeg-bundle/bin/ffmpeg /out/'

"$DOCKER_BIN" run --rm \
  -u "$(id -u):$(id -g)" \
  -v "$OUTPUT_DIR:/out" \
  "$IMAGE_TAG" \
  bash -lc '
    for dir in /usr/lib/x86_64-linux-gnu /usr/lib /usr/lib64; do
      if compgen -G "$dir/libx264.so*" >/dev/null 2>&1; then
        cp -a "$dir"/libx264.so* /out/
        exit 0
      fi
    done
    echo "ERROR: could not locate libx264 in the container" >&2
    exit 1
  '

patchelf --set-rpath '$ORIGIN' "$OUTPUT_DIR/ffmpeg"
for lib_path in "$OUTPUT_DIR"/libav*.so* "$OUTPUT_DIR"/libsw*.so* "$OUTPUT_DIR"/libx264*.so*; do
  if [[ -f "$lib_path" ]] && file -b "$lib_path" | grep -q ELF; then
    patchelf --set-rpath '$ORIGIN' "$lib_path"
  fi
done

write_output_readme

for required_lib in libavcodec.so.62 libavutil.so.60 libswscale.so.9; do
  if [[ ! -e "$OUTPUT_DIR/$required_lib" ]]; then
    echo "Required FFmpeg library missing from bundle: $required_lib" >&2
    exit 1
  fi
done

if ! file "$OUTPUT_DIR/ffmpeg" | grep -q ELF; then
  echo "ffmpeg binary is not a valid ELF executable" >&2
  exit 1
fi

if ! compgen -G "$OUTPUT_DIR/libx264.so.[0-9]*" >/dev/null; then
  echo "libx264.so.<version> not found in output bundle" >&2
  exit 1
fi

for lib_path in \
  "$OUTPUT_DIR/libavcodec.so.62" \
  "$OUTPUT_DIR/libavutil.so.60" \
  "$OUTPUT_DIR/libswscale.so.9"; do
  assert_glibc_floor "$lib_path"
done

echo "Ubuntu 22.04 FFmpeg bundle created at $OUTPUT_DIR"
