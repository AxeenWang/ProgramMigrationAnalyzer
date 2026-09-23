# Codex 任務：完成 WPF POC「程式移植分析儀 - C#、4GL」

## 0. 執行原則

請直接完成一個可執行、可 Demo、可 Build 的 WPF POC，不要只做 Mockup、Wireframe、範例片段或 TODO 專案。

不要向使用者反覆確認細節。未明確處請採合理工程假設並繼續完成。

完成時必須：

1. 建立完整 Solution 與所有必要專案/檔案。
2. Restore NuGet packages。
3. 實際執行 Build。
4. 修正所有可修正的編譯錯誤，直到 Solution 可成功 Build。
5. 建立 10 份可實際分析的假資料。
6. 建立 README.md，說明架構、執行方法、POC 限制。
7. 不要過度工程化；優先完成可操作的 Happy Path。
8. 所有核心功能都必須是真實可操作，不可只用靜態畫面假裝完成。

---

# 1. 程式名稱

**程式移植分析儀 - C#、4GL**

英文 Solution / Namespace 建議使用：

`ProgramMigrationAnalyzer`

---

# 2. POC 目標

建立一套 Windows WPF 工具，可匯入：

- 舊版 .NET Framework C# 原始碼
- 4GL 原始碼

使用者可：

1. 開啟單一原始碼檔案。
2. 開啟包含多個原始碼的資料夾。
3. 預覽原始碼。
4. 按下「開始分析」。
5. 取得程式結構分析結果。
6. 自動產生 Markdown `.md` 分析報告。
7. 在程式內預覽 Markdown。
8. 選擇 `.NET 8` 或 `.NET 10` 作為轉譯目標。
9. 根據分析結果產生 C#/.NET 現代化程式碼草稿。
10. 在程式內預覽轉譯後程式碼。
11. 查看 Migration Assessment。
12. 查看分析與轉譯 Log。

第一階段 POC 不要求做到 100% 自動語意等價移植。

重點是建立可擴充的：

`Source -> Parser -> Intermediate Model -> Analyzer -> Markdown -> Translator`

完整流程。

---

# 3. 技術規格

## Application

- Windows Desktop
- WPF
- App Target Framework：`.NET 10`
- Target Framework Moniker：`net10.0-windows`
- C#
- Nullable enabled
- ImplicitUsings enabled

## Pattern

使用 MVVM。

優先套件：

- `CommunityToolkit.Mvvm`

## C# Parser

使用：

- `Microsoft.CodeAnalysis.CSharp`
- Roslyn Syntax Tree / Semantic-friendly abstraction

不要求建立完整 MSBuild Workspace。

POC 可直接對 `.cs` source text 進行 SyntaxTree 分析。

## Code Preview

優先使用：

- `AvalonEdit`

原始碼與轉譯結果都使用唯讀/可選取的 code editor 預覽。

## Markdown

使用：

- `Markdig`

將 Markdown render 為 HTML。

## Markdown Preview

優先：

- `Microsoft.Web.WebView2`

若 WebView2 初始化失敗，程式不可 Crash。

可以顯示友善錯誤訊息或 fallback 到純 Markdown/Text 預覽。

## JSON

- `System.Text.Json`

---

# 4. Solution 結構

請建立：

```text
ProgramMigrationAnalyzer/
│
├─ ProgramMigrationAnalyzer.sln
│
├─ src/
│  ├─ ProgramMigrationAnalyzer.App/
│  ├─ ProgramMigrationAnalyzer.Core/
│  ├─ ProgramMigrationAnalyzer.Analysis/
│  ├─ ProgramMigrationAnalyzer.Parsers/
│  ├─ ProgramMigrationAnalyzer.Translation/
│  └─ ProgramMigrationAnalyzer.Infrastructure/
│
├─ samples/
│  ├─ 4gl/
│  └─ csharp-framework/
│
├─ output/
│
└─ README.md
```

專案責任：

## ProgramMigrationAnalyzer.App

- WPF UI
- Views
- ViewModels
- Commands
- User interaction
- Code preview
- Markdown preview

## ProgramMigrationAnalyzer.Core

放共用 domain model / contracts。

例如：

- `SourceDocument`
- `SourceLanguage`
- `AnalysisResult`
- `ProgramUnit`
- `FunctionInfo`
- `ParameterInfo`
- `SqlStatementInfo`
- `DependencyInfo`
- `MigrationIssue`
- `AnalysisTag`
- `MigrationAssessment`
- `TranslationResult`

Interfaces：

- `ISourceParser`
- `ISourceAnalyzer`
- `IMarkdownReportGenerator`
- `ISourceTranslator`

## ProgramMigrationAnalyzer.Analysis

- Migration rules
- Deprecated API detection
- SQL classification
- Dependency detection
- Migration assessment calculation
- Markdown report generator

## ProgramMigrationAnalyzer.Parsers

包含：

- `CSharpSourceParser`
- `Informix4GlParser`

未來要能再加：

- Genero
- Progress/OpenEdge ABL
- 其他 4GL dialect

不要把 4GL parser 寫死在 UI。

## ProgramMigrationAnalyzer.Translation

- Translation orchestration
- `.NET 8` / `.NET 10` target selection
- Legacy C# modernization prototype
- 4GL -> modern C# scaffold generation

## ProgramMigrationAnalyzer.Infrastructure

- File loader
- Encoding detection/fallback
- Output writer
- Folder scanning
- App services

---

# 5. Intermediate Analysis Model

核心設計要求：

C# 與 4GL 不可各自直接產 Markdown。

兩者都必須先轉換成共用的 Intermediate Analysis Model。

建議：

```csharp
AnalysisResult
{
    SourceDocument Source;
    SourceLanguage Language;

    string Summary;

    IReadOnlyList<ProgramUnit> ProgramUnits;
    IReadOnlyList<FunctionInfo> Functions;
    IReadOnlyList<SqlStatementInfo> SqlStatements;
    IReadOnlyList<DependencyInfo> Dependencies;
    IReadOnlyList<MigrationIssue> MigrationIssues;
    IReadOnlyList<AnalysisTag> Tags;

    MigrationAssessment Assessment;
}
```

`FunctionInfo` 至少包含：

```text
Name
Kind
ReturnType
Parameters
Summary
CalledFunctions
ReferencedTables
StartLine
EndLine
```

`SqlStatementInfo`：

```text
CommandType
RawSql
Tables
StartLine
Risk
```

`DependencyInfo`：

```text
Name
Category
Description
Risk
```

`MigrationIssue`：

```text
Code
Title
Description
Severity
SuggestedAction
Location
```

Severity：

```text
Info
Low
Medium
High
Critical
```

---

# 6. 支援語言

## 6.1 C#

來源主要是假設：

- .NET Framework 4.x
- 傳統 C#
- ADO.NET
- DataSet
- DataTable
- ConfigurationManager
- WebRequest
- XML
- File I/O

C# parser 至少分析：

- namespace
- class
- interface
- enum
- inheritance
- implemented interfaces
- fields
- properties
- constructors
- methods
- method parameters
- return types
- method invocations
- object creation
- using directives
- SQL 字串
- 常見 legacy dependencies

## 6.2 4GL

POC 第一版使用：

**Informix 4GL 風格語法**

不要求完整 compiler-grade parser。

可使用：

- tokenizer
- regex
- line-based state parser

但程式碼結構要乾淨、可擴充。

至少辨識：

```text
MAIN
END MAIN

FUNCTION
END FUNCTION

DEFINE
GLOBALS
DATABASE

CALL
RETURN

SELECT
INSERT
UPDATE
DELETE

DECLARE
CURSOR
FOREACH

DISPLAY
INPUT

REPORT

IF
ELSE
END IF

FOR
WHILE

BEGIN WORK
COMMIT WORK
ROLLBACK WORK
```

至少可擷取：

- MAIN
- FUNCTION
- parameter
- local variable
- CALL
- SQL
- table name
- database dependency
- UI/display/input dependency
- transaction
- global variable
- report/batch 特性

---

# 7. WPF UI

UI 要簡潔、像工程工具，不需要華麗動畫。

建議主畫面：

```text
┌────────────────────────────────────────────────────────────────────┐
│ 程式移植分析儀 - C#、4GL                                           │
│                                                                    │
│ [開啟檔案] [開啟資料夾]  Language: Auto  Target: .NET 10 ▼         │
│                                      [開始分析] [開始轉譯]          │
├──────────────────┬─────────────────────────────────────────────────┤
│ 檔案 / 專案      │ 原始碼 | 分析結果 | Markdown | 轉譯程式 | Diff │
│                  │                                                 │
│ CustomerQuery    │                                                 │
│ OrderEntry       │                                                 │
│ ...              │                                                 │
│                  │                                                 │
│ Tags             │                                                 │
│ Database         │                                                 │
│ SQL              │                                                 │
│ Business Logic   │                                                 │
│ High Risk        │                                                 │
├──────────────────┴─────────────────────────────────────────────────┤
│ Migration Assessment                                               │
│ Functions | SQL | Dependencies | Deprecated | Manual Review        │
├────────────────────────────────────────────────────────────────────┤
│ Progress / Status / Log                                            │
└────────────────────────────────────────────────────────────────────┘
```

## 必要功能

### Toolbar

- 開啟檔案
- 開啟資料夾
- Language：Auto / C# / 4GL
- Target：.NET 8 / .NET 10
- 開始分析
- 開始轉譯
- 開啟 Output 資料夾（若方便可實作）

### 左側

檔案 Tree/List。

至少顯示：

- 檔名
- Language
- 分析狀態

點擊檔案切換目前預覽內容。

### 主區域 Tabs

必須至少：

1. 原始碼
2. 分析結果
3. Markdown
4. 轉譯程式
5. Diff

Diff POC 可先用簡單 line-based side-by-side 顯示。

不需要實作 Git 等級 diff engine。

### Bottom / Side Assessment

顯示：

```text
Functions
SQL Statements
External Dependencies
Deprecated APIs
Manual Review
Migration Score
```

### Log

分析期間要顯示：

```text
Loading source...
Detecting language...
Parsing...
Analyzing SQL...
Checking legacy APIs...
Generating Markdown...
Completed.
```

UI 不可在分析時完全 Freeze。

使用 async/await。

---

# 8. Language Detection

Auto 模式至少：

`.cs` => C#

以下副檔名視為 4GL：

```text
.4gl
.per
```

若無法判斷：

顯示 Unknown，不可 Crash。

---

# 9. Markdown Report

分析完成後自動輸出 `.md`。

輸出資料夾：

```text
output/
```

檔名：

```text
{SourceFileName}.{SourcePathHash}.analysis.md
```

例如：

```text
CustomerQuery.4gl.{SourcePathHash}.analysis.md
```

第二階段以完整來源路徑的 SHA-256 前 16 位十六進位字元作為 `SourcePathHash`，避免不同目錄的同名來源檔互相覆寫。

Markdown 必須包含：

```markdown
# 程式分析報告

## 基本資訊

- 原始檔案
- 語言
- 分析時間
- 建議目標框架

## 程式用途摘要

## 程式架構

## Functions / Subroutines

### FunctionName

- 用途
- Parameters
- Return
- Calls
- Tables
- Source Lines

## SQL / Database Access

## External Dependencies

## Business Rules / Detected Behavior

## Migration Issues

## Migration Assessment

## 建議移植方向
```

Markdown 內容不可全部是假資料。

必須來自實際 parser/analyzer 結果。

---

# 10. Migration Analysis Rules

第一版採規則式分析。

C# 至少檢查以下 legacy pattern：

## ConfigurationManager

偵測：

```text
System.Configuration.ConfigurationManager
ConfigurationManager.AppSettings
ConfigurationManager.ConnectionStrings
```

建議：

```text
Microsoft.Extensions.Configuration / IConfiguration
```

Severity：

`Medium`

---

## WebRequest

偵測：

```text
WebRequest
HttpWebRequest
```

建議：

```text
HttpClient
```

Severity：

`Medium`

---

## BinaryFormatter

偵測：

```text
BinaryFormatter
```

Severity：

`Critical`

Suggested action：

不可直接照搬；改用安全 serialization format。

---

## System.Web

Severity：

`High`

建議：

重新設計為 ASP.NET Core 對應功能。

---

## DataSet / DataTable

Severity：

`Low` 或 `Medium`

說明：

可在 .NET 使用，但建議依新架構評估 DTO / ORM / Dapper。

---

## SqlConnection / SqlCommand

辨識為：

`Data Access`

不可直接標成錯誤。

---

## File I/O

辨識：

```text
File
Directory
StreamReader
StreamWriter
```

Tag：

`File System`

---

## COM

辨識：

```text
ComImport
Marshal.GetActiveObject
Microsoft.Office.Interop
```

Risk：

High

---

# 11. Migration Assessment

Assessment 不是 AI 工時估算。

是規則式 POC 指標。

至少計算：

```text
FunctionCount
SqlStatementCount
DependencyCount
DeprecatedApiCount
ManualReviewCount
MigrationScore
RiskLevel
```

`MigrationScore` 0~100。

定義：

100 = 自動轉換相對單純

0 = 高度人工處理

可以使用簡單規則：

```text
Score = 100
- High issue * 12
- Critical issue * 20
- Medium issue * 5
- external dependency * 2
- global variable * 3
```

Clamp 0~100。

RiskLevel：

```text
80~100 Low
60~79 Medium
30~59 High
0~29 Critical
```

畫面需明確註明：

`規則式 POC 評估指標`

避免讓人誤認為是真實工時準確率。

---

# 12. Translation Engine

POC 不要求真正做到 production-grade source-to-source compiler。

但不能完全是假輸出。

必須依 AnalysisResult / Intermediate Model 產生合理 C# scaffold。

Target：

- .NET 8
- .NET 10

預設：

`.NET 10`

---

# 13. 4GL -> C# Translation

例如：

```4gl
FUNCTION calc_total(price, qty)
    RETURN price * qty
END FUNCTION
```

至少可轉成類似：

```csharp
public decimal CalcTotal(decimal price, int qty)
{
    return price * qty;
}
```

無法確定型別時：

```csharp
object?
```

並加入：

```csharp
// TODO(Migration): Verify original 4GL type.
```

4GL：

```text
SELECT ...
```

可產：

```csharp
// TODO(Migration): Replace legacy SQL with repository / Dapper / EF Core.
// Original SQL:
// SELECT ...
```

若分析到 Customer / Order / Inventory 等 table，可合理建立：

```text
Models/
Repositories/
Services/
```

概念性 code sections。

至少預覽一份完整、可讀 C# output。

---

# 14. Legacy C# Modernization

舊 C# 不需要重新編譯成完全不同架構。

至少做到：

- 保留可辨識 class/method
- 對 legacy API 加 migration comment
- WebRequest 提示 HttpClient
- ConfigurationManager 提示 IConfiguration
- SQL / ADO.NET 加 repository modernization 建議
- 產生 Target Framework header/comment

例如：

```csharp
// Migration Target: .NET 10
// Generated by Program Migration Analyzer POC
```

---

# 15. Tags

建立常用標籤資料。

至少：

## Language

```text
C#
4GL
SQL
```

## Architecture

```text
UI
Business Logic
Data Access
Batch
Report
Utility
```

## Database

```text
SELECT
INSERT
UPDATE
DELETE
Transaction
Cursor
```

## Dependency

```text
Database
File System
Web Service
DLL
COM
Configuration
XML
```

## Migration

```text
Compatible
Refactor Required
Deprecated API
Unsupported API
Manual Review
High Risk
```

## Business

```text
Validation
Calculation
Approval
Settlement
Customer
Order
Inventory
Invoice
```

分析時自動附加適當 tags。

不可只有靜態 tag list。

---

# 16. 4GL 假資料

建立：

```text
samples/4gl/
```

以下 5 份檔案。

每份至少約 50~120 行，有足夠分析價值。

---

## 16.1 CustomerQuery.4gl

情境：

客戶資料查詢。

包含：

- DATABASE
- MAIN
- DEFINE
- CALL
- FUNCTION
- SELECT
- DISPLAY
- IF
- 客戶狀態判斷

Table：

```text
customer
customer_contact
```

Business Rules：

- CustomerId 不可空
- status = "D" 不可操作
- 查無資料顯示訊息

---

## 16.2 OrderEntry.4gl

情境：

訂單建立。

包含：

- INPUT
- INSERT
- UPDATE
- transaction
- BEGIN WORK
- COMMIT WORK
- ROLLBACK WORK
- CALL
- validation

Tables：

```text
orders
order_detail
customer
```

---

## 16.3 InventoryCheck.4gl

情境：

庫存查詢。

包含：

- SELECT
- cursor
- FOREACH
- warehouse
- inventory
- reorder level 計算

Tables：

```text
inventory
warehouse
product
```

---

## 16.4 InvoiceReport.4gl

情境：

發票報表。

包含：

- REPORT
- SELECT
- JOIN
- calculation
- DISPLAY / PRINT-like report behavior

Tables：

```text
invoice
invoice_detail
customer
```

---

## 16.5 BatchSettlement.4gl

情境：

夜間批次結算。

包含：

- batch
- FOREACH
- UPDATE
- transaction
- error handling
- CALL
- settlement calculation

Tables：

```text
payment
settlement
account
```

---

# 17. .NET Framework C# 假資料

建立：

```text
samples/csharp-framework/
```

5 份。

每份約 60~150 行。

風格要像真實舊系統，不要刻意寫成現代 C#。

---

## 17.1 CustomerService.cs

包含：

- `System.Data.SqlClient`
- `SqlConnection`
- `SqlCommand`
- `SqlDataReader`
- inline SQL
- `ConfigurationManager.ConnectionStrings`

用途：

查詢客戶。

---

## 17.2 OrderService.cs

包含：

- `DataSet`
- `DataTable`
- `SqlDataAdapter`
- INSERT / UPDATE
- transaction-like logic

用途：

訂單維護。

---

## 17.3 InventoryManager.cs

包含：

- `ConfigurationManager.AppSettings`
- File logging
- inventory calculation
- SQL

用途：

庫存管理。

---

## 17.4 InvoiceGenerator.cs

包含：

- `File`
- `StreamWriter`
- XML
- `DataTable`
- invoice export

用途：

發票檔案產生。

---

## 17.5 LegacyApiClient.cs

包含：

- `HttpWebRequest` 或 `WebRequest`
- XML serialization / XML parsing
- synchronous call
- legacy configuration

用途：

呼叫外部 API。

---

# 18. Analysis Result UI

分析結果頁至少以易讀結構呈現：

```text
Program Summary

Architecture

Functions
  - GetCustomer
  - ValidateCustomer
  - SaveOrder

SQL
  - SELECT customer
  - INSERT orders

Dependencies
  - SQL Server
  - ConfigurationManager
  - File System

Migration Issues
  - ConfigurationManager
  - WebRequest

Tags
```

不要只把 JSON dump 到畫面。

可以使用 TreeView / ListView / Cards。

---

# 19. Error Handling

以下情況不可 Crash：

- 空檔案
- 不支援副檔名
- malformed 4GL
- malformed C#
- Markdown render error
- WebView2 runtime 問題
- Output folder 無法寫入
- 檔案編碼問題

錯誤需：

- 顯示 Message
- 寫入 Log
- 保持 UI 可操作

---

# 20. Encoding

File loader 至少嘗試：

1. UTF-8
2. UTF-8 BOM
3. 系統預設 encoding fallback

若需讀 Big5，可註冊：

```csharp
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
```

並嘗試：

```text
950
```

這是舊系統 POC，Big5 支援有價值。

---

# 21. Output

分析時建立：

```text
output/
```

例如：

```text
output/
├─ CustomerQuery.4gl.{SourcePathHash}.analysis.md
├─ CustomerQuery.4gl.{SourcePathHash}.net10.cs
├─ CustomerService.cs.{SourcePathHash}.analysis.md
└─ CustomerService.cs.{SourcePathHash}.net10.cs
```

.NET 8：

```text
.net8.cs
```

.NET 10：

```text
.net10.cs
```

---

# 22. 非目標

POC 第一版不要浪費時間做：

- 登入系統
- Database backend
- Cloud deployment
- 多使用者
- 權限系統
- 微服務
- Docker
- Kubernetes
- 完整 AI Agent
- 完整 4GL compiler
- 完整 semantic equivalence proof
- 完整 Git diff engine
- 複雜動畫
- Theme framework 大改造

第一版目標：

**Windows 本機可 Demo。**

---

# 23. UI 視覺要求

整體：

- 簡潔
- 現代
- 工程工具感
- 淺色即可
- 不要 Material Design 過度裝飾
- 不要巨型 icon
- 不要過多 border

建議配色：

- neutral gray
- blue accent
- warning orange
- error red

但不要為了美術犧牲 POC 功能。

---

# 24. MVVM 要求

不要把主要邏輯全部寫在 `MainWindow.xaml.cs`。

ViewModel 至少：

```text
MainViewModel
SourceDocumentViewModel
AnalysisViewModel
MigrationAssessmentViewModel
```

Services 透過 constructor injection 或簡單 composition root 建立。

不強制導入完整 DI container。

若 `Microsoft.Extensions.DependencyInjection` 能讓結構更乾淨，可以使用。

---

# 25. Commands

至少：

```text
OpenFileCommand
OpenFolderCommand
AnalyzeCommand
TranslateCommand
SaveMarkdownCommand
```

可使用：

```text
RelayCommand
AsyncRelayCommand
```

---

# 26. Analyze Flow

`AnalyzeCommand`：

```text
Validate selection
↓
Load source
↓
Detect language
↓
Select parser
↓
Parse to AnalysisResult
↓
Run migration rules
↓
Calculate assessment
↓
Generate Markdown
↓
Save Markdown
↓
Update UI
```

Log 每階段更新。

---

# 27. Translate Flow

`TranslateCommand`：

```text
Require AnalysisResult
↓
Read target framework
↓
Run translator
↓
Generate C#
↓
Save .cs
↓
Update Code Preview
↓
Update Diff
```

---

# 28. Suggested Contracts

可以依需要調整，但請維持這種方向：

```csharp
public interface ISourceParser
{
    bool CanParse(SourceDocument source);
    Task<AnalysisResult> ParseAsync(
        SourceDocument source,
        CancellationToken cancellationToken = default);
}
```

```csharp
public interface IMarkdownReportGenerator
{
    string Generate(AnalysisResult result);
}
```

```csharp
public interface ISourceTranslator
{
    bool CanTranslate(SourceLanguage language);

    Task<TranslationResult> TranslateAsync(
        AnalysisResult analysis,
        TargetFramework targetFramework,
        CancellationToken cancellationToken = default);
}
```

---

# 29. Target Framework Model

```csharp
public enum MigrationTargetFramework
{
    Net8,
    Net10
}
```

UI 顯示：

```text
.NET 8
.NET 10
```

預設：

`.NET 10`

---

# 30. README

README.md 至少寫：

```text
# Program Migration Analyzer POC

## Overview

## Features

## Architecture

## Solution Structure

## Requirements

Windows
.NET 10 SDK
WebView2 Runtime

## Build

dotnet restore
dotnet build

## Run

dotnet run --project ...

## Sample Data

## Supported C# Analysis

## Supported 4GL Syntax

## Translation Behavior

## Known Limitations

## Future Work
```

---

# 31. Build 驗證

完成程式碼後務必執行：

```powershell
dotnet --info
dotnet restore
dotnet build ProgramMigrationAnalyzer.sln
```

如果 build fail：

**繼續修，不要停在錯誤狀態。**

若某 NuGet package 與 .NET 10 不相容：

使用目前可用、相容的 stable package/version。

不要為了死守套件版本導致 POC 無法 build。

---

# 32. Runtime 驗證

若環境可執行 Windows WPF：

啟動 App。

至少驗證：

## Scenario A

```text
Open:
samples/4gl/CustomerQuery.4gl

Analyze

Expected:
- Language = 4GL
- Functions > 0
- SQL > 0
- CUSTOMER dependency/tag
- Markdown generated
```

## Scenario B

```text
Open:
samples/csharp-framework/LegacyApiClient.cs

Analyze

Expected:
- Language = C#
- WebRequest/HttpWebRequest migration issue
- XML dependency
- Markdown generated
```

## Scenario C

```text
Translate CustomerQuery.4gl
Target = .NET 10
```

Expected：

- C# output
- `.net10.cs`
- Code preview updated

---

# 33. Acceptance Criteria

POC 完成的定義：

- [ ] Solution 存在
- [ ] WPF app 可 Build
- [ ] 主畫面不是空殼
- [ ] 可開啟 `.cs`
- [ ] 可開啟 `.4gl`
- [ ] 可開啟資料夾
- [ ] 可預覽 source
- [ ] 可偵測語言
- [ ] C# Roslyn parser 有實際作用
- [ ] 4GL parser 有實際作用
- [ ] 使用共用 Intermediate Model
- [ ] 可分析 classes/functions
- [ ] 可分析 function calls
- [ ] 可分析 SQL
- [ ] 可分析 tables
- [ ] 可分析 dependencies
- [ ] 可偵測至少數個 legacy .NET API
- [ ] Tags 由分析結果自動產生
- [ ] Migration Assessment 有實際計算
- [ ] 可產 Markdown
- [ ] Markdown 寫入 output
- [ ] App 內可預覽 Markdown
- [ ] 可選 .NET 8
- [ ] 可選 .NET 10
- [ ] 可產生 translation prototype
- [ ] translation 寫入 output
- [ ] App 內可預覽 translation code
- [ ] 有基本 Diff
- [ ] 有 Progress / Status / Log
- [ ] 有 5 份 4GL sample
- [ ] 有 5 份 legacy C# sample
- [ ] README 完成
- [ ] `dotnet restore` 成功
- [ ] `dotnet build` 成功

---

# 34. 實作優先順序

若需要分階段實作，依以下順序，但請盡量在本次任務完成全部：

## Milestone 1

建立 Solution / projects / references。

## Milestone 2

Core models + contracts。

## Milestone 3

File loading + language detection。

## Milestone 4

Roslyn C# parser。

## Milestone 5

Informix 4GL parser prototype。

## Milestone 6

Migration analyzer + tags + assessment。

## Milestone 7

Markdown generator。

## Milestone 8

Translation prototype。

## Milestone 9

WPF MVVM UI。

## Milestone 10

10 sample files。

## Milestone 11

Build / debug / fix。

## Milestone 12

README。

---

# 35. 最重要的設計原則

不要做成：

```text
Input Source
↓
LLM magic
↓
Output Code
```

要做成：

```text
Source
↓
Parser
↓
Intermediate Analysis Model
├─ Structure
├─ Functions
├─ Calls
├─ SQL
├─ Dependencies
├─ Tags
└─ Migration Issues
↓
┌─────────────────────┐
│ Markdown Generator  │
└─────────────────────┘
↓
Analysis Report

以及：

Intermediate Analysis Model
↓
Translator
↓
.NET 8 / .NET 10 C# Prototype
```

如此未來才能加入：

- AI semantic analysis
- Vector search
- RAG
- LLM translation
- Automated compilation
- Unit test generation
- Database migration
- Genero parser
- Progress ABL parser

目前 POC 不需要上述功能。

---

# 36. 最終交付

完成後不要只回覆「已完成」。

請最後輸出：

```text
1. 建立了哪些 projects
2. 主要功能完成狀態
3. Build 結果
4. 主程式啟動方式
5. Sample 路徑
6. Output 路徑
7. POC 已知限制
8. 建議下一階段
```

但最重要的是：

**檔案與程式本身必須真的已經建立並 Build，不要只描述應該怎麼做。**
