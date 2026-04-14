# VPet 插件开发踩坑记录

## 1. NuGet 包版本错误

**问题**
Claude Code 生成的 `.csproj` 引用了不存在的版本：
```
找不到版本为 (>= 2.0.22) 的包 VPet-Simulator.Core
```

**原因**
NuGet 上 VPet-Simulator.Core 最新版本为 `1.1.0.60`，并不存在 2.x 版本。

**解决**
修改 `.csproj`：
```xml
<PackageReference Include="VPet-Simulator.Core" Version="1.1.0.60" />
```

---

## 2. IPlugin / IMainWindow 命名空间找不到

**问题**
```
error CS0246: 未能找到类型或命名空间名"IPlugin"
error CS0246: 未能找到类型或命名空间名"IMainWindow"
```

**原因**
VPet 把插件接口单独放在 `VPet-Simulator.Windows.Interface` 包里，而不是 `VPet-Simulator.Core`。
插件入口需要继承 `MainPlugin` 抽象类，构造函数由 VPet 反射注入 `IMainWindow`。

**解决**
- 额外安装 `VPet-Simulator.Windows.Interface` NuGet 包
- 正确 using：`using VPet_Simulator.Core;`
- ToolBar 注册在 `LoadDIY()` 生命周期钩子里，通过 `FindName("ToolBar")` 拿控件实例
- 右键菜单接入方式：`mainWindow.Main.ContextMenu.Items.Add(...)`

---

## 3. mod 目录和 Plugins 目录的误解

**问题**
VPet 没有独立的 `Plugins/` 目录，找不到放插件的位置。

**原因**
VPet 插件和普通 mod（动画、食物等资源）走同一套 `mod/` 目录，通过文件夹内的 DLL 识别代码插件。

**正确目录结构**
```
VPet安装目录\mod\
├── 0000_core\        ← 自带核心，不要动
└── 9001_VPetCompanion\   ← 自建插件文件夹
    ├── info.lps
    ├── VPetCompanion.Plugin.dll
    ├── VPetCompanion.Core.dll
    └── config.json
```

**找到 mod 目录的方法**
启动 VPet → 右键宠物 → 系统 → MOD → MOD管理 → 点击「所在文件夹」

**开发期推荐：用符号链接代替手动复制**
以管理员身份运行 cmd：
```cmd
mklink /d "VPet安装目录\mod\9001_VPetCompanion" "你的项目\VPetCompanion.Plugin\bin\Debug\net8.0"
```
之后每次 `dotnet build` 重启 VPet 即可生效，无需手动复制文件。

---

## 4. info.lps 格式错误（多次反复）

**问题**
VPet 启动报 `NullReferenceException`，调用栈在读取 `info.lps` 的各行。

**原因逐步排查**

| 错误行 | 原因 |
|---|---|
| `FindLine("vupmod").Info` | 根节点字段名写成了 `modinfo`，实际应为 `vupmod` |
| `FindLine("intro").Info` | `intro` 没有独立成行，被合并在同一行导致解析失败 |
| `FindSub("gamever").InfoToInt` | 误以为需要块格式 `{ }`，实际 `FindSub` 也读 `key#value:|` 格式 |

**正确的 info.lps 格式**

对照 Core 官方 info.lps 的写法：
```
vupmod#VPetCompanion:|author#你的名字:|gamever#11000:|ver#10000:|
intro#AI陪伴聊天插件:|
```

**格式规则总结**
- 每个字段格式：`key#value:|`
- 基础信息（vupmod、author、gamever、ver）写在第一行
- `intro` 必须单独放在第二行
- `gamever` 和 `ver` 填整数，对应版本 `1.10.00` 写 `11000`

---

## 5. config.json 路径反斜杠问题

**问题**
Windows 路径直接粘贴会报错。

**原因**
JSON 中反斜杠需要转义。

**正确写法**
```json
{
  "modelPath": "C:\\Users\\Administrator\\Downloads\\Qwen3-8B-Q4_K_M.gguf",
  "contextSize": 4096,
  "gpuLayerCount": 0,
  "maxTokens": 512,
  "temperature": 0.8
}
```

---

## 快速检查清单

每次部署插件前确认：
- [ ] `info.lps` 存在于插件文件夹
- [ ] `info.lps` 第一行含 `vupmod`、`author`、`gamever`、`ver`
- [ ] `info.lps` 第二行单独写 `intro`
- [ ] `config.json` 路径反斜杠已转义（`\\`）
- [ ] 插件文件夹命名格式为 `数字_名称`（如 `9001_VPetCompanion`）
- [ ] VPet 重启后在 MOD管理 确认加载成功
