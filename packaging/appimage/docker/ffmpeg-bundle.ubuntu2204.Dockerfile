FROM ubuntu:22.04

ARG FFMPEG_VERSION=8.1.2

RUN apt-get update && apt-get install -y \
    build-essential \
    pkg-config \
    ca-certificates \
    curl \
    xz-utils \
    nasm \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /tmp

RUN curl -LO "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" \
    && tar -xf "ffmpeg-${FFMPEG_VERSION}.tar.xz"

WORKDIR /tmp/ffmpeg-${FFMPEG_VERSION}

RUN ./configure \
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
    --disable-avdevice \
    --disable-avfilter \
    --disable-avformat \
    --disable-postproc \
    --disable-swresample \
    --enable-shared \
    --enable-decoder=hevc \
    --enable-parser=hevc \
    && make -j"$(nproc)" \
    && make install \
    && test ! -e /opt/ffmpeg-bundle/lib/libavdevice.so \
    && test ! -e /opt/ffmpeg-bundle/lib/libavfilter.so \
    && test ! -e /opt/ffmpeg-bundle/lib/libavformat.so \
    && test ! -e /opt/ffmpeg-bundle/lib/libswresample.so
