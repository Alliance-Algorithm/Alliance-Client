 # Ubuntu 22.04 容器里手工验证低基线 FFmpeg Runtime 的详细步骤

  ## Summary

  - 目标是在 ubuntu:22.04 容器里，手工编出一套只服务于 Alliance.VideoWorker 的最小 FFmpeg 8 shared runtime。
  - 这套 runtime 的输出目录固定为：
      - /home/floatpigeon/Alliance/Alliance-Client/packaging/vendor/ffmpeg/linux-x64-ubuntu2204

  - 手工验证通过后，再用现有 build-appimage.sh 指向这套 bundle 生成新的 AppImage。

  ## Step 1: 启动容器并挂载仓库

  - 宿主机执行：

  cd /home/floatpigeon/Alliance/Alliance-Client

  mkdir -p packaging/vendor/ffmpeg/linux-x64-ubuntu2204

  docker run --rm -it \
    -v /home/floatpigeon/Alliance/Alliance-Client:/workspace \
    -w /workspace \
    ubuntu:22.04 \
    bash

  - 如果当前用户没有 Docker 权限，就把上面的 docker 改成 sudo docker。

  ## Step 2: 在容器里安装最小构建依赖

  - 进入容器后执行：

  apt-get update
  apt-get install -y \
    build-essential \
    pkg-config \
    ca-certificates \
    curl \
    xz-utils \
    nasm

  ## Step 3: 下载并解压 FFmpeg 8.1.2 源码

  - 容器内执行：

  cd /tmp
  curl -LO https://ffmpeg.org/releases/ffmpeg-8.1.2.tar.xz
  tar -xf ffmpeg-8.1.2.tar.xz
  cd ffmpeg-8.1.2

  ## Step 4: 用“最小 shared runtime”参数配置 FFmpeg

  - 容器内执行：

  ./configure \
    --prefix=/opt/ffmpeg-bundle \
    --libdir=/opt/ffmpeg-bundle/lib \
    --shlibdir=/opt/ffmpeg-bundle/lib \
    --disable-everything \
    --disable-autodetect \
    --disable-programs \
    --disable-doc \
    --disable-static \
    --disable-network \
    --disable-hwaccels \
    --disable-bsfs \
    --disable-filters \
    --disable-muxers \
    --disable-demuxers \
    --disable-devices \
    --enable-shared \
    --enable-avcodec \
    --enable-avutil \
    --enable-swscale \
    --enable-decoder=hevc \
    --enable-parser=hevc

  ## Step 5: 编译并安装到容器内前缀目录

  - 容器内执行：

  make -j"$(nproc)"
  make install

  ## Step 6: 清空并导出新的 low-baseline bundle

  - 容器内执行：

  rm -rf /workspace/packaging/vendor/ffmpeg/linux-x64-ubuntu2204/*
  cp -a /opt/ffmpeg-bundle/lib/. /workspace/packaging/vendor/ffmpeg/linux-x64-ubuntu2204/

  ## Step 7: 在容器内验证这套 runtime

  - 先看生成了哪些主库：

  cd /workspace/packaging/vendor/ffmpeg/linux-x64-ubuntu2204
  ls -lh

  - 期望至少有：
      - libavcodec.so.62
      - libavutil.so.60
      - libswscale.so.9

  - 检查依赖链：

  ldd libavcodec.so.62
  ldd libavutil.so.60
  ldd libswscale.so.9

  - 期望结果：
      - 不再出现 libva、libvpl、glib、cairo、rsvg、x264、x265、jxl 这类重依赖

  - 检查最高 glibc 版本要求：

  readelf --version-info libavcodec.so.62 | grep -o 'GLIBC_[0-9.]*' | sort -Vu | tail -n 1
  readelf --version-info libavutil.so.60 | grep -o 'GLIBC_[0-9.]*' | sort -Vu | tail -n 1
  readelf --version-info libswscale.so.9 | grep -o 'GLIBC_[0-9.]*' | sort -Vu | tail -n 1

  - 期望结果：
      - 都不高于 GLIBC_2.35

  ## Step 8: 退出容器

  - 容器内执行：

  exit

  ## Step 9: 用新 bundle 重新构建 AppImage

  - 回到宿主机后执行：

  cd /home/floatpigeon/Alliance/Alliance-Client

  FFMPEG_BUNDLE_DIR=/home/floatpigeon/Alliance/Alliance-Client/packaging/vendor/ffmpeg/linux-x64-ubuntu2204 \
  APPIMAGETOOL=/home/floatpigeon/Tools/appimagetool/appimagetool-x86_64.AppImage \
  APPIMAGE_RUNTIME_FILE=/tmp/runtime-x86_64 \
  APPIMAGE_FILENAME=Alliance-Client-linux-x64-ubuntu2204-test.AppImage \
  bash packaging/appimage/build-appimage.sh

  ## Step 10: 对新包做最小验证

  - 宿主机执行：

  ls -lh artifacts/appimage/Alliance-Client-linux-x64-ubuntu2204-test.AppImage

  - 目标机执行：

  chmod +x ./Alliance-Client-linux-x64-ubuntu2204-test.AppImage
  APPIMAGE_EXTRACT_AND_RUN=1 ./Alliance-Client-linux-x64-ubuntu2204-test.AppImage

  - 如果还要进一步确认：

  ./Alliance-Client-linux-x64-ubuntu2204-test.AppImage --appimage-extract
  ldd squashfs-root/usr/lib/alliance-client/worker/ffmpeg/libavcodec.so.62 | grep 'not found'

  - 期望结果：
      - 没有任何输出
      - 不再出现 GLIBC_2.43 not found
      - 不再出现 System.NotSupportedException
      - 视频状态能进入 VIDEO READY

  ## Assumptions

  - 兼容基线固定为 Ubuntu 22.04 / glibc 2.35
  - 先做手工验证，不在这一步引入新的仓库脚本或 Dockerfile
  - 当前 worker 只需要 HEVC decoder + parser + swscale，不需要完整发行版 FFmpeg 功能集
