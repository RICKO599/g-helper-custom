# CHANGELOG — 基于 G-Helper 0.288 的二次开发

本仓库是 [seerge/g-helper](https://github.com/seerge/g-helper) **v0.288** 的二次开发版本，
非官方分支。原始版权归 seerge 及贡献者所有，遵循 GPL v3。

开发目标机型：**ROG Zephyrus G14 (GA401QM)**，Ryzen 9 5900HS + RTX 3060 Laptop。
日常负载：AUTOSAR/MATLAB 编译（长时间 CPU 满载）、偶尔 GPU 渲染。

---

## 痛点 1：锁屏后人离开了，机器还在全速跑

**现象**
G-Helper 原版不会因为锁屏改变性能模式。用增强模式编译完代码，按 Win+L 走开，
机器继续以 Turbo 功耗和风扇曲线运行，房间里一直有风扇声，电池也在掉。

**为什么原版不做**
原版把"性能模式"视为用户显式选择的状态，不介入会话生命周期，只在插拔电源、
休眠唤醒等事件上做自动切换。

**方案**
监听 `SystemEvents.SessionSwitch`：
- `SessionLock` → 记录当前模式到 `mode_before_lock`，切到静音模式
- `SessionUnlock` / `SessionLogon` → 读出记录并恢复，然后清除

关键实现细节（都是踩过的坑）：

| 问题 | 处理 |
|---|---|
| `Modes.SetCurrent()` 会顺手改写 `performance_<电源>`（按电源记住的模式），导致锁屏一次后插电也变静音 | 切静音后立即把该键写回原值，静音只是临时状态 |
| 远程桌面接入（`ConsoleConnect`）会误触发恢复 | 只认 `SessionUnlock` / `SessionLogon` |
| 锁屏期间程序被重启，`mode_before_lock` 残留 | 恢复时判断 `saved == 当前模式` 则直接清除，不动作 |
| `lock_silent: false` 因 `int.TryParse` 失败回落到默认值，关不掉 | 改用字符串判断，同时接受 `0` 和 `"false"` |

**开关**：主界面底部「锁屏静音」复选框，默认开启。

---

## 痛点 2：GPU 风扇不跟 CPU，两个风扇转速差一大截

**现象**
G14 两个风扇共享同一套热管。CPU 88°C / 4500RPM 时，GPU 只有 66°C / 3900RPM。
CPU 单烤场景下 GPU 风扇明显偷懒，整体散热效率浪费。

**为什么原版做不到**
EC 固件对每个风扇是**独立的硬件闭环**：CPU 风扇读 CPU 传感器查 CPU 曲线，
GPU 风扇读 GPU 传感器查 GPU 曲线。软件层面没有"让 GPU 风扇读 CPU 传感器"的接口。

**探索过程（三条路，前两条走不通）**

1. `DevsGPUFan (0x00110023)` 的 min/max 范围接口
   → 返回 `OK`，但硬件完全不采纳（实测 GPU 仍 2700RPM）。**放弃。**

2. 按 CPU 曲线插值算目标转速，写进 GPU 曲线
   → 失败。EC 对 CPU 风扇有自己的滞回和平滑：88°C 时曲线值 37%，实际风扇 45%。
   曲线值 ≠ 实际转速。**放弃。**

3. 闭环增量调节（读 GPU 实际值，逼近 CPU）
   → 发散。EC 的风扇爬升需要数秒，读数永远滞后于上一次写入，形成正反馈，
   duty 一路降到 37% 而 GPU 仍高于 CPU。**放弃。**

**最终方案：开环 + 实测标定**

每 3 秒读一次 CPU 风扇的**实际占空比**（`GetFan(CPU)`），换算成 GPU 曲线占空比，
把 GPU 曲线整表重写成恒定值（16 字节，温度轴任意，输出全等于目标 duty），
这样无论 GPU 传感器读多少，EC 都输出同一个转速。

换算里有两个必须处理的量纲/非线性问题：

- **量纲**：`GetFan` 返回的是 `RPM/100`（满速约 72–90），而风扇曲线要的是 0–100 的占空比。
  直接当百分比用会系统性偏低约 28%。
- **非线性**：EC 的 duty→RPM 响应不是线性的。实测 34% → ~3850、47% → ~4450，
  即有效满速随转速升高而缩小。用仿射补偿 `full = 138 − cpuFan` 拟合。

**实测效果**（`%AppData%\GHelper\log.txt`）
```
FanLink: CPU 45 / GPU 44 -> duty 48%
FanLink: CPU 44 / GPU 44 -> duty 47%
FanLink: CPU 45 / GPU 43 -> duty 48%
```
中高转速段偏差 ≤100RPM，低转速段 200–400RPM（EC 在该段非线性最强，`GetFan` 也只有
100RPM 分辨率，继续拟合只会追噪声）。

**安全阀**：即使 CPU 凉，GPU 超过 75°C 强制至少 50%、超过 85°C 强制至少 80%。
否则纯 GPU 负载（渲染/训练）时 CPU 不热，风扇会被 CPU 曲线压到停转。

**开关**：主界面底部「双扇联动」复选框，默认关闭。联动时 Fans 页的 GPU 曲线图锁定
（标题显示「CPU 联动中，已锁定」），避免改了又被覆盖。

---

## 痛点 3：自己编译的版本，点一次更新就没了

**现象**
改了代码的 G-Helper 在启动时会检查 GitHub releases，提示有新版本；点更新会用官方
签名版覆盖 `GHelper.exe`，所有修改丢失。

**方案**
写入 `skip_updates: 1`（G-Helper 自带的隐藏开关，`AutoUpdateControl.cs:69` 在发请求前
直接 return）。界面上的「更新」按钮仍可点，但只会用浏览器打开 releases 页面，不会覆盖本地文件。

**验证**：生效前每次启动都有 `Latest version 0.288.0.0` 或 `Failed to check for updates`；
写入后重启，日志中不再出现任何更新检查记录。

---

## 痛点 4：联动时 GPU 曲线还能拖，拖完几秒就被覆盖

**现象**
双扇联动开启后，Fans 页的 GPU 曲线图仍然可以编辑，但改动会在 3 秒内被联动逻辑重写，
用户看到自己拖的点又跳回去。

**方案**
联动开启时 `chartGPU.Enabled = false`，标题追加「CPU 联动中，已锁定」；关闭联动立即解锁。
切模式时同步刷新（`InitAll()` 末尾统一调用），避免切完模式状态不一致。

---

## 构建

需要 .NET 10 SDK（原项目 `GHelper.csproj` 的 `TargetFramework` 为 `net10.0-windows`）：

```powershell
dotnet publish app/GHelper.sln -c Release -r win-x64 -p:PublishSingleFile=true --no-self-contained
```

产物：`app/bin/x64/Release/net10.0-windows/win-x64/publish/GHelper.exe`（约 5.8MB）

`app/NuGet.config` 配置了国内镜像，可按需删除。

## 许可

GPL v3，与原项目一致。详见 `LICENSE`。
