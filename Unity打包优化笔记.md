# Unity 打包优化笔记

## 一、打包脚本概述

项目使用基于 **YooAsset** 的自动化打包脚本 `ReleaseTools.cs`，主要功能：

- **AB包构建**：`BuildAssetBundle()` / `BuildCurrentPlatformAB()`
- **热更DLL构建**：`BuildDll()` / `BuildDLLCommand.BuildAndCopyDlls()`
- **完整包体构建**：Windows / Android / iOS
- **版本号规则**：`yyyy-MM-dd-{当天总分钟数}`，每次打包都不同

支持平台：Android、iOS、Windows、macOS、Linux、WebGL、Switch、PS4、PS5

---

## 二、打包慢的原因分析

### 根本原因：IL2CPP 的 AOT 编译机制

打包时控制台显示 link GameAssembly 时间最长，这是 IL2CPP 的链接阶段。

### 次要原因：版本号导致增量缓存失效

```csharp
// GetBuildPackageVersion() 每次返回不同的时间戳
BuildImpDebug(..., $"/../Builds/DebugWindows{GetBuildPackageVersion()}/Debug_Windows.exe");
```

每次输出路径不同，IL2CPP 找不到上次的缓存，**每次都是全量编译**。

---

## 三、优化方案

### 优先级排序

| 优先级 | 操作 | 预期收益 |
|--------|------|----------|
| ⭐⭐⭐ | Debug包改用 Mono | 直接消灭 GameAssembly 链接，快3-5倍 |
| ⭐⭐⭐ | AB包输出目录固定，让增量生效 | 节省10分钟+ |
| ⭐⭐⭐ | 代码没变就用"不打bundle"版本 | 节省10分钟+ |
| ⭐⭐ | 修复 SwitchActiveBuildTarget 的 Bug | 节省3-5分钟 |
| ⭐⭐ | Debug包改用Mono编译 | 节省3-5分钟 |
| ⭐ | 减少 AssetDatabase.Refresh() | 节省1-2分钟 |

### Debug 包改用 Mono

```csharp
public static void BuildImpDebug(BuildTargetGroup buildTargetGroup, BuildTarget buildTarget, string locationPathName)
{
    // 强制切换为 Mono，Debug包不需要 IL2CPP
    PlayerSettings.SetScriptingBackend(buildTargetGroup, ScriptingImplementation.Mono2x);

    EditorUserBuildSettings.SwitchActiveBuildTarget(buildTargetGroup, buildTarget);
    AssetDatabase.Refresh();
    // ...后续不变
}
```

### Debug 包路径固定（去掉时间戳）

```csharp
// 改前：每次新目录，缓存失效
$"/../Builds/DebugWindows{GetBuildPackageVersion()}/Debug_Windows.exe"

// 改后：固定目录，缓存生效
$"/../Builds/DebugWindows/Debug_Windows.exe"
```

---

## 四、Mono vs IL2CPP 原理

### Mono — JIT（Just-In-Time Compilation）

程序运行时，IL 字节码由 **JIT 编译器**在**方法首次被调用时**编译为本地机器码，编译结果缓存在内存中，同一方法后续调用直接执行缓存的机器码。

```
调用方法A
  → 检查缓存：未命中
  → JIT编译器将方法A的IL编译为机器码
  → 写入内存缓存
  → 执行

再次调用方法A
  → 检查缓存：命中
  → 直接执行
```

关键点：
- 编译发生在**运行时**，以**方法**为单位
- 进程退出后机器码**不持久化**，下次运行重新编译
- 首次调用有编译开销，即 **JIT Warm-up**

### IL2CPP — AOT（Ahead-Of-Time Compilation）

在**构建阶段**，将全部 IL 字节码转换为 C++ 源码，再由 C++ 编译器编译链接为本地机器码，最终产物是 **GameAssembly.dll**。

```
构建阶段：
IL字节码 → [IL2CPP转换器] → C++源码 → [C++编译器] → 目标文件 → [链接器] → GameAssembly.dll

运行阶段：
直接加载并执行 GameAssembly.dll，无任何编译过程
```

关键点：
- 编译发生在**构建时**，以**整个程序**为单位
- 运行时**无编译开销**，直接执行本地机器码
- 链接阶段需要将所有目标文件合并，**这是打包慢的根本原因**

### 本质区别

> JIT 是将编译开销分摊到运行时每次方法调用，AOT 是将编译开销集中到构建阶段一次性完成。

---

## 五、JIT Warm-up 机制

Mono 单次运行内会越跑越流畅，但不跨进程持久化：

| | 单次运行内 | 跨进程 |
|--|--|--|
| Mono | 越跑越快（Warm-up结束后） | 每次重新 Warm-up |
| IL2CPP | 始终全速 | 始终全速 |

表现为：
- 首帧卡顿
- 加载第一个场景慢
- 第一次触发某个技能/特效有轻微卡顿

进程退出后机器码销毁，下次启动重新经历 Warm-up。

---

## 六、何时必须用 IL2CPP

| 场景 | 原因 |
|------|------|
| iOS | 苹果禁止 JIT，强制要求 |
| WebGL | 浏览器不支持 Mono |
| PS4 / PS5 / Switch | 平台规范强制要求 |
| 正式对外发布 | 防反编译，安全性要求 |
| 性能压测包 | 贴近真实运行环境 |

---

## 七、推荐工作流

```
日常开发
  ↓
Mono Debug包（5分钟内出包）
  ↓
功能验证没问题
  ↓
IL2CPP Release包（正式发布用）
  ↓
上线
```

| 包类型 | 推荐后端 | 原因 |
|--------|------|------|
| Windows Debug | Mono | 只是内部测试 |
| Windows Release | IL2CPP | 对外发布，安全+性能 |
| Android | IL2CPP | 防反编译，性能更好 |
| iOS | IL2CPP | 苹果强制，没得选 |
