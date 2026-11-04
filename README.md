# G-Helper Custom — 基于 G-Helper 的二次开发

> **本项目是 [seerge/g-helper](https://github.com/seerge/g-helper) 的二次开发版本（非官方分支）**，
> 基于其 **v0.288** 源码修改，针对 ROG Zephyrus G14 (GA401QM) 做功能增强。
> 原始版权归 seerge 及贡献者所有。原项目采用 **GPL v3**，本仓库同样以 GPL v3 发布。

## 快速开始（无需编译）

仓库里带了预编译的 `dist/GHelper.exe`，拉下来就能用：

1. 下载或克隆本仓库
   ```
   git clone https://github.com/RICKO599/g-helper-custom.git
   ```
2. 在仓库根目录运行安装脚本（普通权限即可）
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1
   ```
   脚本会：检测 .NET 10 Desktop Runtime（缺失时用 winget 自动安装）→ 复制到 `%LOCALAPPDATA%\GHelper` → 启动。
3. 启动后在主界面底部勾选「开机自启」，注册开机启动任务

> G-Helper 需要管理员权限才能做充电限制、GPU 模式切换等操作，首次运行会自行请求提权。
> 想完全手动安装：把 `dist\GHelper.exe` 复制到 `%LOCALAPPDATA%\GHelper\GHelper.exe` 直接运行也可以。

## 解决的痛点

原版 G-Helper 已经很完整，但在这台机器的实际使用中有四个具体问题：

| # | 痛点 | 后果 | 本项目的做法 |
|---|---|---|---|
| 1 | 锁屏后不降速 | 编译完按 Win+L 走开，机器继续 Turbo 全速跑，风扇吵、掉电 | 锁屏切静音、解锁恢复原模式 |
| 2 | GPU 风扇不跟 CPU | 两风扇共享热管，CPU 88°C/4500RPM 时 GPU 只有 3900RPM，散热效率浪费 | 双扇联动：GPU 跟随 CPU 实际转速 |
| 3 | 自编译版被官方更新覆盖 | 点一次更新，所有修改丢失 | 禁用自动更新检查 |
| 4 | 联动时 GPU 曲线仍可编辑 | 拖完几秒被覆盖，用户困惑 | 联动时锁定 GPU 曲线图并标注原因 |

详细的现象、根因、失败方案与实测数据见 **[CHANGELOG.md](CHANGELOG.md)**。

## 功能一览

| 功能 | 说明 | 涉及文件 |
|---|---|---|
| 锁屏静音 | 锁屏时切到静音性能模式，解锁后恢复原模式。主界面底部有开关 | `Program.cs`、`Settings.cs`、`Settings.Designer.cs` |
| 双扇联动 | GPU 风扇跟随 CPU 风扇的实际转速（重写 EC 风扇曲线 + 开环标定），Fans 页 GPU 曲线在联动时锁定 | `Fan/FanLinkControl.cs`、`Fans.cs`、`Mode/ModeControl.cs` |
| 禁用自动更新 | 默认写入 `skip_updates`，避免自编译版本被官方更新覆盖 | config 项 |
| 主界面开关 | 底部新增「锁屏静音」「双扇联动」两个复选框 | `Settings.Designer.cs` |

### 双扇联动的实现要点

EC 对每个风扇是独立硬件闭环（CPU 风扇读 CPU 传感器、GPU 风扇读 GPU 传感器），且在本机型上
`DevsGPUFan (0x00110023)` 的 min/max 范围接口返回成功但硬件不采纳。

因此实现方式为：每 3 秒读一次 CPU 风扇实际转速，按经验换算成 GPU 曲线占空比，把 GPU 风扇曲线
重写为一条恒定输出表。换算系数含仿射补偿（EC 的 duty→RPM 响应非线性），可通过 config 的
`fan_link_full` 调整截距（默认 138）。

## 构建

需要 .NET 10 SDK：

```powershell
dotnet publish app/GHelper.sln -c Release -r win-x64 -p:PublishSingleFile=true --no-self-contained
```

产物：`app/bin/x64/Release/net10.0-windows/win-x64/publish/GHelper.exe`

`app/NuGet.config` 配置了国内镜像，可按需删除或替换。

## 许可

GPL v3，与原项目一致。详见 `LICENSE`。
