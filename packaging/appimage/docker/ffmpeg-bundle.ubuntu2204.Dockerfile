FROM ubuntu:22.04

ARG FFMPEG_VERSION=8.1.2

RUN apt-get update && apt-get install -y \
    build-essential \
    pkg-config \
    ca-certificates \
    curl \
    xz-utils \
    nasm \
    libx264-dev \
    libx11-dev \
    libxext-dev \
    libxfixes-dev \
    libdrm-dev \
    libva-dev \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /tmp

RUN curl -LO "https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz" \
    && tar -xf "ffmpeg-${FFMPEG_VERSION}.tar.xz"

WORKDIR /tmp/ffmpeg-${FFMPEG_VERSION}

RUN ./configure \
    --prefix=/opt/ffmpeg-bundle \
    --libdir=/opt/ffmpeg-bundle/lib \
    --shlibdir=/opt/ffmpeg-bundle/lib \
    --enable-libx264 \
    --enable-gpl \
    --enable-indev=x11grab \
    --enable-indev=kmsgrab \
    --enable-vaapi \
    --disable-doc \
    --disable-static \
    --enable-shared \
    && make -j"$(nproc)" \
    && make install \
    && test -x /opt/ffmpeg-bundle/bin/ffmpeg
