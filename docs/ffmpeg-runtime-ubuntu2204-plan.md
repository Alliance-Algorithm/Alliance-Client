# Ubuntu 22.04 基线 FFmpeg Runtime 重建方案

## 1. 背景

当前 `AppImage` 已经解决了 **FFmpeg 传递依赖链缺失** 的问题：打包脚本会自动把 `libavcodec.so.62`、`libavutil.so.60`、`libswscale.so.9` 的依赖递归收进 `worker/ffmpeg/`。

但在对方机器上仍然无法出图，根因已经进一步定位为：

- 当前 FFmpeg runtime 是在 **Arch Linux / glibc 2.43** 环境中构建或采集的
- 包内部分 `.so` 需要较新的 `GLIBC_*` 版本，例如 `GLIBC_2.43`
- 对方机器的系统基线更低，虽然依赖链齐全，但运行时仍会因为 `glibc` 版本不足而崩溃

这说明问题已经不是“缺库”，而是“**构建基线过高**”。

## 2. 目标

为 `Alliance.VideoWorker` 提供一套 **Ubuntu 22.04 / glibc 2.35 基线** 的 FFmpeg 8 shared runtime，使生成的 `AppImage` 在较新的 Ubuntu / Debian 系目标机上可以稳定运行。

本方案只覆盖当前实际需要的最小能力：

- HEVC 解码
- HEVC parser
- `swscale` 转换为 `BGRA`

不保留完整发行版 FFmpeg 的大量编码器、图形组件和硬件加速依赖。

## 3. 方案原则

### 3.1 固定低基线构建环境

使用 `docker` 在 `ubuntu:22.04` 容器中构建 FFmpeg runtime，不再从宿主 Arch 系统直接复制 `.so` 文件。

### 3.2 构建最小 shared runtime

FFmpeg 只启用 worker 当前需要的最小组件：

- `libavcodec`
- `libavutil`
- `libswscale`
- `hevc` decoder
- `hevc` parser

关闭其他大部分组件，避免把不必要的依赖带进 bundle，例如：

- `libva`
- `libvpl`
- `glib`
- `cairo`
- `rsvg`
- `x264`
- `x265`
- `jxl`

### 3.3 与现有 AppImage 脚本兼容

现有 `packaging/appimage/build-appimage.sh` 保持主流程不变，通过 `FFMPEG_BUNDLE_DIR` 切换到新的低基线 runtime 目录即可。

## 4. 目录设计

建议新增以下结构：

```text
packaging/
├── appimage/
│   ├── build-appimage.sh
│   ├── build-ffmpeg-bundle.sh
│   └── docker/
│       └── ffmpeg-bundle.ubuntu2204.Dockerfile
└── vendor/
    └── ffmpeg/
        ├── linux-x64/
        └── linux-x64-ubuntu2204/
```

说明：

- `linux-x64/` 保留当前目录，作为历史或临时 bundle
- `linux-x64-ubuntu2204/` 作为新的正式分发基线目录

## 5. Docker 基线构建方案

### 5.1 容器镜像

基础镜像固定为：

- `ubuntu:22.04`

### 5.2 容器内安装的构建依赖

仅安装最小构建链，示意如下：

- `build-essential`
- `pkg-config`
- `ca-certificates`
- `curl`
- `xz-utils`
- `yasm` 或 `nasm`

不安装系统发行版自带的完整 FFmpeg 开发包，避免重新引入多余依赖。

### 5.3 FFmpeg 版本

固定使用 FFmpeg 8.x，和当前项目里的 `FFmpeg.AutoGen 8.1.0` 保持 ABI 一致。

推荐直接锁定一个明确版本，例如：

- `FFmpeg 8.1.2`

### 5.4 配置参数

FFmpeg 的 `configure` 建议固定为“最小 shared runtime”路线，核心原则如下：

- `--disable-everything`
- `--disable-autodetect`
- `--disable-programs`
- `--disable-doc`
- `--disable-static`
- `--enable-shared`
- `--enable-avcodec`
- `--enable-avutil`
- `--enable-swscale`
- `--enable-decoder=hevc`
- `--enable-parser=hevc`

不额外启用：

- encoders
- muxers
- demuxers
- devices
- hwaccels
- filters
- bsfs
- network

目标是只产出当前 worker 解码路径需要的 `.so`。

## 6. 产物导出要求

容器构建完成后，导出到：

- `packaging/vendor/ffmpeg/linux-x64-ubuntu2204/`

这里至少应包含：

- `libavcodec.so.62`
- `libavutil.so.60`
- `libswscale.so.9`

如果该最小 runtime 仍有少量非系统级依赖，也一并导出到这个目录。

## 7. 与 AppImage 构建链的衔接

现有 `build-appimage.sh` 不改主入口，只改使用的新 bundle 目录。

构建命令统一为：

```bash
FFMPEG_BUNDLE_DIR=/home/floatpigeon/Alliance/Alliance-Client/packaging/vendor/ffmpeg/linux-x64-ubuntu2204 \
APPIMAGETOOL=/home/floatpigeon/Tools/appimagetool/appimagetool-x86_64.AppImage \
APPIMAGE_RUNTIME_FILE=/tmp/runtime-x86_64 \
bash packaging/appimage/build-appimage.sh
```

正式发布时不要继续使用当前 Arch 机器采集出来的 `linux-x64/` bundle。

## 8. 验证标准

### 8.1 基线检查

对新 bundle 中的主库执行：

```bash
readelf --version-info libavcodec.so.62
readelf --version-info libavutil.so.60
readelf --version-info libswscale.so.9
```

要求：

- 最高 `GLIBC_*` 版本不高于 `GLIBC_2.35`

### 8.2 依赖检查

对新 bundle 中的主库执行：

```bash
ldd libavcodec.so.62
ldd libavutil.so.60
ldd libswscale.so.9
```

要求：

- 不再出现当前 Arch 方案里那种庞大的桌面/硬件加速依赖链
- 至少不再依赖 `libva`、`libvpl`、`glib`、`cairo`、`rsvg`、`x264`、`x265`、`jxl` 这类重依赖

### 8.3 AppImage 检查

用新 bundle 生成 `AppImage` 后，在目标机上执行：

```bash
./Alliance-Client-linux-x64.AppImage --appimage-extract
ldd squashfs-root/usr/lib/alliance-client/worker/ffmpeg/libavcodec.so.62 | grep 'not found'
```

要求：

- 无输出

### 8.4 运行验证

目标机上启动 AppImage：

```bash
APPIMAGE_EXTRACT_AND_RUN=1 ./Alliance-Client-linux-x64.AppImage
```

要求：

- 不再出现 `GLIBC_2.43 not found`
- 不再出现 `System.NotSupportedException: Specified method is not supported`
- 不再出现 `Video worker did not connect within 5 seconds`
- 视频状态能进入 `VIDEO READY`

## 9. 风险与边界

### 9.1 本方案不是“任意 Linux 全兼容”

当前基线定为 `Ubuntu 22.04 / glibc 2.35`，因此更老的系统仍然可能不兼容。

### 9.2 本方案只解决 FFmpeg runtime 来源问题

不涉及：

- `FFmpeg.AutoGen` 版本调整
- 视频协议调整
- `Alliance.VideoWorker` 解码逻辑调整

### 9.3 当前自动递归收集依赖链逻辑保留

它仍然可作为安全网存在，但预期在低基线最小 runtime 下，真正被收集到的额外库会显著减少。

## 10. 最终建议

正式分发时，应当把：

- “在 Ubuntu 22.04 容器中构建出的最小 FFmpeg runtime”

作为唯一正式来源，替换当前从 Arch 宿主机采集出来的 bundle。

否则即使依赖链齐全，也仍然会因为 `glibc` 基线过高而在他人电脑上失败。
