# freethreaded 版（MoviePilot-V3-T）环境准备指南

> 本文档介绍 freethreaded 版后端（Python 3.14.7t 免费线程解释器）运行所需的**本机编译环境**。
> freethreaded 版 Python 的第三方依赖（如 psycopg 等）没有官方预编译二进制，首次安装依赖时由 uv 在本机实时编译源码，因此需要先准备 C 编译工具链。

## 为什么需要这些环境

freethreaded 版 Python（3.14t）较新，大部分第三方库尚未发布 freethreaded 版二进制轮子（wheel），安装依赖时会走**源码编译**路径：

- `psycopg` 需要 **PostgreSQL 的 libpq**（编译时链接 + 运行时加载）
- 其他 Rust / C 扩展需要 **MSVC 工具链**（cl.exe / link.exe）与 **Rust** 编译器

以下步骤缺一不可，请按顺序安装。

## 步骤 1：安装 Visual Studio Installer（C++ 桌面开发）

1. 打开官网下载页：<https://visualstudio.microsoft.com/zh-hans/downloads/>，选择 **Insiders** 版本下载
2. 安装完成并打开 Visual Studio Installer
3. 在「工作负载」中勾选 **使用 C++ 的桌面开发**（Desktop development with C++）
4. 组件选择可参考：[Rust，msvc工具链最小安装，VisualStudio Installer里怎么选](https://zhuanlan.zhihu.com/p/678846997)

> MSVC 工具链是编译 Rust / Python C 扩展的基础，缺失时编译会报 `link.exe not found` / `cl.exe not found`。

## 步骤 2：安装 Rust

1. 打开官网下载页：<https://rust-lang.org/zh-CN/tools/install/>
2. x86-64 平台选择下载 **RUSTUP-INIT.EXE (X64)**
3. 双击运行，按默认选项安装（MSVC toolchain）
4. 安装完成后新开终端验证：`rustc --version`、`cargo --version`

## 步骤 3：配置国内源

编辑 Rust 配置 `%USERPROFILE%\.cargo\config.toml`（不存在则新建）：

```toml
[source.crates-io]
replace-with = 'rsproxy-sparse'

[source.rsproxy-sparse]
registry = "sparse+https://rsproxy.cn/index/"

[registries.rsproxy]
index = "https://rsproxy.cn/crates.io-index"

[net]
git-fetch-with-cli = true
```

同时设置环境变量（可选，加速 rustup 自身组件下载）：

```text
RUSTUP_DIST_SERVER=https://rsproxy.cn
RUSTUP_UPDATE_ROOT=https://rsproxy.cn/rustup
```

配置完成后新开终端验证：`cargo search serde` 能正常拉取索引即表示生效。

## 步骤 4：安装 PostgreSQL

1. 打开官网下载页：<https://www.enterprisedb.com/downloads/postgres-postgresql-downloads>，选择 **Windows x86-64** 版本下载安装
2. 安装完成后，把 **`<安装路径>\PostgreSQL\<版本>\bin`** 目录加入系统环境变量 `PATH`（如 `C:\Program Files\PostgreSQL\17\bin`）

> 加入 PATH 的目的是让 Rust 编译 psycopg 时能找到 **libpq**（`pg_config`），是编译通过的必要条件。

## PostgreSQL 与 MP 的实际使用说明

**面板实际运行时，MP 无法直接切换到 PostgreSQL 数据库**：后端 Python 进程加载 DLL 时不走系统环境变量 `PATH`，因此即使把 `bin` 加入了 PATH，运行期加载 `libpq.dll` 仍会失败。

如需在 freethreaded 版中使用 PostgreSQL，需在 `server\MoviePilot-V3-T\app\main.py` 顶部手动注册 DLL 搜索目录（**必须在 `import psycopg` 之前执行**，路径换成你实际的 PostgreSQL 路径）：

```python
import os
# 必须在 import psycopg 之前执行 换成你实际的postgresql路径
os.add_dll_directory(r"D:\postgresql-17.11-1-windows-x64-binaries\pgsql\bin")
```

## 重要声明

- **psycopg 官方至今没有打包 freethreaded 版的二进制文件**，当前使用的是源码编译版本，某些场景下数据库可能会发生**脏数据**
- 如果你比较看重数据，**最好的保护就是勤备份数据库**
- **freethreaded 现阶段适用于体验**，不建议在生产环境长期运行
