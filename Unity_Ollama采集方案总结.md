# Unity + Ollama AI 响应时间采集方案 —— 开发记录

## 项目目标

在多台不同配置的电脑上运行 Unity 程序，记录每次与本地 Ollama AI 对话的响应时间，并连同设备硬件信息一起汇总到一张 Excel 表格中，用于横向比较不同硬件配置对 AI 响应速度的影响。

---

## 问题一：Unity 中如何保存采集数据到文件

### 问题描述
最初的想法是把数据保存到 `StreamingAssets` 文件夹，因为 Unity 开发者通常知道这个路径。

### 问题所在
`StreamingAssets` 在打包后是**只读**的，运行时无法写入文件。

### 解决方案
改用 `Application.persistentDataPath`，这是 Unity 官方推荐的运行时读写路径，在各平台均可正常写入。

| 平台 | 实际路径 |
|---|---|
| Windows | `C:\Users\用户名\AppData\LocalLow\公司名\项目名\` |
| macOS | `~/Library/Application Support/公司名/项目名/` |
| Android | `/data/data/包名/files/` |

### 关键代码
```csharp
string filePath = Path.Combine(Application.persistentDataPath, "文件名.json");
File.WriteAllText(filePath, json);
```

---

## 问题二：如何获取机器名、内存、CPU、GPU 信息

### 问题描述
最初参考普通 C# 方案，打算使用 `System.Management` 来查询硬件信息（如 `Win32_OperatingSystem`）。

### 问题所在
Unity 有自己内置的 `SystemInfo` 类，可以跨平台获取硬件信息，不需要引入额外的 NuGet 包，更简洁且兼容性更好。

### 解决方案
直接使用 Unity 内置 API：

| 需求 | Unity API |
|---|---|
| CPU 型号 | `SystemInfo.processorType` |
| GPU 型号 | `SystemInfo.graphicsDeviceName` |
| 总内存 (MB) | `SystemInfo.systemMemorySize` |

> ⚠️ `SystemInfo` 只提供**总内存**，没有空闲内存接口。若需要空闲内存，Windows 平台可用 `#if UNITY_STANDALONE_WIN` 条件编译引入 `System.Management`。

### 关键代码
```csharp
var info = new MachineData
{
    cpuName       = SystemInfo.processorType,
    gpuName       = SystemInfo.graphicsDeviceName,
    totalMemoryMB = SystemInfo.systemMemorySize,
};
```

---

## 问题三：如何计算 AI 响应时间

### 问题描述
最初 `collectedAt` 字段只记录了当前时刻，但实际需要的是**从发送消息到收到回复经过了多少秒**。

### 解决方案
用 `System.Diagnostics.Stopwatch` 夹住 `ChatAsync` 调用，精确计算耗时。

### 关键代码
```csharp
var stopwatch = System.Diagnostics.Stopwatch.StartNew();

string result = await OllamaManager.Instance.ChatAsync(ollamaMessages);

stopwatch.Stop();
double responseSeconds = stopwatch.Elapsed.TotalSeconds;

MachineInfo.Save(responseSeconds);
```

---

## 问题四：文件命名冲突

### 问题描述
为了区分不同设备，最初用 CPU + GPU 名称组合作为文件名。

### 问题所在
两台配置完全相同的电脑，CPU + GPU 组合一致，文件名会重复，互相覆盖。

### 尝试方案
加入时间戳 + 随机短码：
```
20250409_153022_a3f9c1_info.json
```
这样确保每次运行都生成唯一文件，绝对不会重名。

### 但随即引出新问题
见问题五。

---

## 问题五：同一台设备多次对话产生多个 JSON 文件

### 问题描述
加入随机码后，同一台电脑连续对话多次，会生成多个 JSON 文件，汇总时需要处理大量零散文件，逻辑混乱。

### 正确需求
**一台设备对应一个 JSON 文件**，每次对话的记录追加到该文件的数组中。

### 解决方案
- 文件名回归用 CPU + GPU 组合（同一台机器始终定位到同一个文件）
- 文件内部用数组 `records` 存储每次对话记录
- 每次保存时先读取已有内容，追加新记录后再写回

### 最终 JSON 结构
```json
{
  "cpuName": "Intel Core i7-12700K",
  "gpuName": "NVIDIA GeForce RTX 3080",
  "totalMemoryMB": 32768,
  "records": [
    { "time": "2025-04-09 15:30:22", "responseSeconds": 3.47 },
    { "time": "2025-04-09 15:31:05", "responseSeconds": 2.91 },
    { "time": "2025-04-09 15:33:18", "responseSeconds": 4.12 }
  ]
}
```

### 关键代码
```csharp
// 读取已有记录，没有则新建
var deviceData = File.Exists(filePath)
    ? JsonUtility.FromJson<DeviceData>(File.ReadAllText(filePath))
    : new DeviceData
      {
          cpuName       = cpuName,
          gpuName       = gpuName,
          totalMemoryMB = SystemInfo.systemMemorySize,
          records       = new List<ConversationRecord>()
      };

// 追加本次记录
deviceData.records.Add(new ConversationRecord
{
    time            = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    responseSeconds = Math.Round(responseSeconds, 2)
});

File.WriteAllText(filePath, JsonUtility.ToJson(deviceData, prettyPrint: true));
```

---

## 最终完整方案

### 架构总览

```
各台电脑（Unity 运行）                    你的电脑
──────────────────────                   ──────────────────────
每次 AI 对话结束
  → 计时 → 写入 JSON
  
  i7_RTX3080_info.json  ──┐
  i9_RTX4090_info.json  ──┤  手动复制到 C:\Collect
  i5_RTX3060_info.json  ──┘
                                          运行 Aggregator.cs
                                            → 生成 汇总.xlsx
```

### Unity 端完整代码（MachineInfo.cs）

```csharp
using System;
using System.IO;
using UnityEngine;

public static class MachineInfo
{
    public static void Save(double responseSeconds)
    {
        string cpuName = SystemInfo.processorType;
        string gpuName = SystemInfo.graphicsDeviceName;

        string safeName = $"{cpuName}_{gpuName}"
                          .Replace(" ", "_")
                          .Replace("/", "-")
                          .Replace("\\", "-");
        string filePath = Path.Combine(Application.persistentDataPath, $"{safeName}_info.json");

        var deviceData = File.Exists(filePath)
            ? JsonUtility.FromJson<DeviceData>(File.ReadAllText(filePath))
            : new DeviceData
              {
                  cpuName       = cpuName,
                  gpuName       = gpuName,
                  totalMemoryMB = SystemInfo.systemMemorySize,
                  records       = new System.Collections.Generic.List<ConversationRecord>()
              };

        deviceData.records.Add(new ConversationRecord
        {
            time            = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            responseSeconds = Math.Round(responseSeconds, 2)
        });

        File.WriteAllText(filePath, JsonUtility.ToJson(deviceData, prettyPrint: true));
        Debug.Log($"已追加记录：{filePath}（共 {deviceData.records.Count} 条）");
    }

    [Serializable]
    public class DeviceData
    {
        public string cpuName;
        public string gpuName;
        public int    totalMemoryMB;
        public System.Collections.Generic.List<ConversationRecord> records;
    }

    [Serializable]
    public class ConversationRecord
    {
        public string time;
        public double responseSeconds;
    }
}
```

### 调用位置

```csharp
var ollamaMessages = messages.Select(m => new OllamaMessage(m.role, m.content)).ToList();
OllamaManager.Instance.ModelName = localName;

var stopwatch = System.Diagnostics.Stopwatch.StartNew();
string result = await OllamaManager.Instance.ChatAsync(ollamaMessages);
stopwatch.Stop();

Debug.Log($"OllamaManager :{result}");
MachineInfo.Save(stopwatch.Elapsed.TotalSeconds);

if (result == null)
{
    callback?.Invoke("......", false);
    return;
}
```

### 汇总端完整代码（Aggregator.cs，普通 C# 控制台程序）

NuGet 依赖：`ClosedXML`

```csharp
using System;
using System.IO;
using System.Text.Json;
using ClosedXML.Excel;

class Aggregator
{
    static void Main()
    {
        string inputDir   = @"C:\Collect";
        string outputFile = Path.Combine(inputDir, "汇总.xlsx");

        var files = Directory.GetFiles(inputDir, "*_info.json");
        if (files.Length == 0)
        {
            Console.WriteLine("没有找到任何 json 文件。");
            return;
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("AI响应数据");

        string[] headers = { "CPU 型号", "GPU 型号", "总内存 (MB)", "对话时间", "AI 响应时间 (秒)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.SteelBlue;
            cell.Style.Font.FontColor = XLColor.White;
        }

        int row = 2;
        foreach (var file in files)
        {
            try
            {
                string json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string cpu = root.GetProperty("cpuName").GetString();
                string gpu = root.GetProperty("gpuName").GetString();
                int    mem = root.GetProperty("totalMemoryMB").GetInt32();

                foreach (var record in root.GetProperty("records").EnumerateArray())
                {
                    sheet.Cell(row, 1).Value = cpu;
                    sheet.Cell(row, 2).Value = gpu;
                    sheet.Cell(row, 3).Value = mem;
                    sheet.Cell(row, 4).Value = record.GetProperty("time").GetString();
                    sheet.Cell(row, 5).Value = record.GetProperty("responseSeconds").GetDouble();
                    row++;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"跳过文件 {Path.GetFileName(file)}：{e.Message}");
            }
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(outputFile);
        Console.WriteLine($"汇总完成：{outputFile}（共 {row - 2} 条记录）");
    }
}
```

### 汇总 Excel 格式

| CPU 型号 | GPU 型号 | 总内存 (MB) | 对话时间 | AI 响应时间 (秒) |
|---|---|---|---|---|
| Intel Core i7-12700K | NVIDIA GeForce RTX 3080 | 32768 | 2025-04-09 15:30:22 | 3.47 |
| Intel Core i7-12700K | NVIDIA GeForce RTX 3080 | 32768 | 2025-04-09 15:31:05 | 2.91 |
| Intel Core i9-13900K | NVIDIA GeForce RTX 4090 | 65536 | 2025-04-09 16:00:10 | 1.83 |
