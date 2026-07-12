# Alliance Client AppImage 打包

目标环境只考虑 `linux-x64`，默认目标机有桌面环境，但不要求预装 `dotnet` 或 FFmpeg。

当前仓库已经接入完整打包链路：主程序自包含发布、`Alliance.VideoWorker` 随包发布、FFmpeg 私有目录优先加载，以及 `AppImage` 组装脚本。

## 运行时边界

这份 `AppImage` 会自带：

- `Alliance.Client` 主程序
- `Alliance.VideoWorker` 子进程
- .NET 10 自包含运行时
- 固定版本的 FFmpeg 8 动态库

这份 `AppImage` 仍然依赖宿主机提供：

- `glibc`
- 图形驱动
- X11/Wayland 桌面环境
- `FUSE` 或 `APPIMAGE_EXTRACT_AND_RUN=1`

## 目录约定

AppImage 打包相关文件都在 `packaging/`：

- `packaging/appimage/build-appimage.sh`：主打包脚本
- `packaging/appimage/build-ffmpeg-bundle.sh`：在 Docker 里重建 Ubuntu 22.04 基线 FFmpeg runtime
- `packaging/appimage/AppRun`：AppImage 入口，负责设置 `ALLIANCE_FFMPEG_ROOT` 和 `LD_LIBRARY_PATH`
- `packaging/appimage/alliance-client.desktop`：桌面文件
- `packaging/appimage/alliance-client.svg`：最小占位图标，仅用于满足 `appimagetool` 校验
- `packaging/vendor/ffmpeg/linux-x64-ubuntu2204/`：正式分发使用的 FFmpeg 运行时目录

当前构建方案不要求额外设计图标，但仍保留一个最小占位图标文件，以满足 `appimagetool` 的打包要求。

## 准备 FFmpeg 运行时

正式分发时，先在 Docker 里重建 Ubuntu 22.04 基线的 FFmpeg runtime：

```bash
bash packaging/appimage/build-ffmpeg-bundle.sh
```

默认输出目录：

`packaging/vendor/ffmpeg/linux-x64-ubuntu2204/`

如果你只是临时测试，也可以手动把 Linux x64 的 FFmpeg 8 运行时文件放到：

`packaging/vendor/ffmpeg/linux-x64-ubuntu2204/`

至少需要：

- `libavcodec.so.62`
- `libavutil.so.60`
- `libswscale.so.9`

这 3 个库现在作为 seed 库使用。打包脚本会从构建机上递归收集它们的非系统级依赖链，一并复制进 AppImage 的 `worker/ffmpeg/`。

如果构建机本地缺少其中某个依赖，或者依赖链收集后仍然不完整，脚本会直接失败。

## 构建要求

建议在干净的 `Ubuntu 22.04 x86_64` 环境里执行，避免把开发机环境差异带入产物。

本地需要：

- `dotnet`（用于 `net10.0` 自包含发布）
- `appimagetool`
- `docker`（用于重建 Ubuntu 22.04 基线 FFmpeg runtime）

## 打包命令

默认命令：

```bash
bash packaging/appimage/build-appimage.sh
```

推荐正式流程：

```bash
bash packaging/appimage/build-ffmpeg-bundle.sh

TMPDIR=/home/floatpigeon/Alliance/Alliance-Client/.tmp \
APPIMAGETOOL=/home/floatpigeon/Tools/appimagetool/appimagetool-x86_64.AppImage \
APPIMAGE_RUNTIME_FILE=/tmp/runtime-x86_64 \
APPIMAGE_FILENAME=Alliance-Client-linux-x64-vX.Y.Z.AppImage \
bash packaging/appimage/build-appimage.sh
```

如果之前因为 `/tmp` 配额导致构建失败，正式构建时建议显式设置 `TMPDIR` 到仓库内可写目录，例如上面的 `.tmp`。

常用环境变量：

```bash
CONFIGURATION=Release \
RID=linux-x64 \
APPIMAGETOOL=/path/to/appimagetool \
APPIMAGE_RUNTIME_FILE=/path/to/runtime-x86_64 \
FFMPEG_BUNDLE_DIR=/abs/path/to/ffmpeg-bundle \
OUTPUT_DIR=/abs/path/to/output \
bash packaging/appimage/build-appimage.sh
```

如果当前环境无法让 `appimagetool` 自动下载 `runtime-x86_64`，就手动准备该文件并通过 `APPIMAGE_RUNTIME_FILE` 传给脚本。

默认输出位置：

`artifacts/appimage/Alliance-Client-linux-x64.AppImage`

## 脚本做的事

`build-appimage.sh` 会按下面顺序执行：

1. `dotnet publish src/Alliance.Client/Alliance.Client.csproj -c Release -r linux-x64 --self-contained true`
2. 自动把 `Alliance.VideoWorker` 发布到主程序产物中的 `worker/`
3. 把 FFmpeg bundle 复制到 `worker/ffmpeg/`
4. 组装 `AppDir`
5. 调用 `appimagetool` 生成最终 `AppImage`

## 运行验证

打包完成后建议至少验证：

```bash
APPIMAGE_EXTRACT_AND_RUN=1 ./artifacts/appimage/Alliance-Client-linux-x64.AppImage
```

验证点：

- 主窗口能启动
- 未安装系统 `dotnet` 时仍可运行
- 未安装系统 FFmpeg 时仍可运行
- 推送 UDP 图传后，视频状态能进入 `VIDEO READY`

## 关键实现说明

- 主程序现在优先启动随包的 `worker/Alliance.VideoWorker`
- Worker 现在优先查找自身目录下的 `ffmpeg/`，再查 `ALLIANCE_FFMPEG_ROOT`，最后才回退系统目录
- `dotnet publish` 主程序时，会同步 `publish` 一个自包含的 `Alliance.VideoWorker` 到 `worker/`
- FFmpeg runtime 的正式来源是 `linux-x64-ubuntu2204/`，不再使用旧的 Arch bundle

这样最终用户只需要下载一个 `AppImage` 文件即可启动整个客户端。
