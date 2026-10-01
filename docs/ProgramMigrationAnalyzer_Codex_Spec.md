# Codex 任務：完成 WPF「程式移植分析儀 - C#、4GL」

> 2026-10-01 需求更新：新增「登入成功後才能開啟主程式」；登入設計與後續實作要求見第 37 節。
> 本次更新是設計規格，尚未實作登入功能。第 37 節及相關驗收條件代表後續版本要求，不代表目前程式已具備這些能力。

## 0. 執行原則

請直接完成一個可執行、可 Demo、可 Build 的 WPF 工具，不要只做 Mockup、Wireframe、範例片段或 TODO 專案。

不要向使用者反覆確認細節。未明確處請採合理工程假設並繼續完成。

完成時必須：

1. 建立完整 Solution 與所有必要專案/檔案。
2. Restore NuGet packages。
3. 實際執行 Build。
4. 修正所有可修正的編譯錯誤，直到 Solution 可成功 Build。
5. 建立 10 份可實際分析的假資料。
6. 建立 README.md，說明架構、執行方法、已知限制。
7. 不要過度工程化；優先完成可操作的 Happy Path。
8. 所有核心功能都必須是真實可操作，不可只用靜態畫面假裝完成。

---

# 1. 程式名稱

**程式移植分析儀 - C#、4GL**

英文 Solution / Namespace 建議使用：

`ProgramMigrationAnalyzer`

---

# 2. 工具目標

建立一套 Windows WPF 工具，可匯入：

- 舊版 .NET Framework C# 原始碼
- 4GL 原始碼

使用者必須先完成登入及工具使用資格驗證，才可開啟主畫面並使用以下功能；取消登入則結束程式。認證方式見第 37 節。

登入後使用者可：

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

第一階段不要求做到 100% 自動語意等價移植。

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

目前可直接對 `.cs` source text 進行 SyntaxTree 分析。

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
- LoginWindow、LoginViewModel 與登入／主視窗切換
- 依登入狀態控制操作入口
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
- `IAuthenticationService`、`IUserSession`（登入契約，見第 37 節）

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

- 實際認證來源的 adapter 與必要的連線設定
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

第一版使用：

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

程式啟動先顯示獨立登入視窗；登入成功前不可建立或顯示下列主畫面，也不可預先載入來源檔或執行分析。登入畫面、錯誤提示與視窗生命週期見第 37 節。

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
- 目前登入使用者顯示名稱
- 登出（返回登入視窗；成功登出後不可繼續操作原工作區）

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

Diff 可先用簡單 line-based side-by-side 顯示。

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

是規則式評估指標。

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

`規則式評估指標`

避免讓人誤認為是真實工時準確率。

---

# 12. Translation Engine

目前不要求真正做到 production-grade source-to-source compiler。

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
// Generated by Program Migration Analyzer
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
- 登入失敗、認證服務連線失敗或逾時
- 登入遭取消、認證設定缺漏或登入狀態失效

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

考量舊系統來源，Big5 支援有價值。

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

原始分析功能第一版的非目標如下；2026-10-01 新增登入需求後，登入已納入下一版必要功能，依第 37 節實作：

- 自建帳號資料庫與完整帳號管理後台
- Cloud deployment
- 多使用者協作工作區
- 細粒度角色／功能權限系統（仍須驗證帳號是否可使用本工具）
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

但不要為了美術犧牲核心功能。

---

# 24. MVVM 要求

不要把主要邏輯全部寫在 `MainWindow.xaml.cs`。

ViewModel 至少：

```text
MainViewModel
LoginViewModel
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
LoginCommand
CancelLoginCommand
LogoutCommand
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
Require authenticated session
↓
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
Require authenticated session
↓
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
# Program Migration Analyzer

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

不要為了死守套件版本導致程式無法 build。

---

# 32. Runtime 驗證

若環境可執行 Windows WPF：

啟動 App。

至少驗證：

登入功能完成後，Scenario A／B／C 的桌面驗證均須先登入。另須執行第 37.9 節的登入專項驗證；直接建立主視窗的既有測試不能取代正式啟動流程驗證。

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

目前版本完成的定義：

- [ ] Solution 存在
- [ ] WPF app 可 Build
- [ ] 啟動先顯示登入視窗，未登入不建立或顯示主視窗
- [ ] 實際帳號驗證及工具使用資格檢查成功後，才可進入主畫面
- [ ] 登入失敗、逾時及設定缺漏時保持未登入，取消登入能完全結束程式
- [ ] 未登入時操作命令不可執行，直接呼叫命令也不能繞過檢查
- [ ] 登出清除登入狀態與記憶體中的工作區，重新登入才可繼續使用
- [ ] 第 37.9 節登入專項與既有分析／轉譯回歸檢查通過
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

## Milestone 13（新增登入需求）

依第 37.10 節完成本機帳號部署、登入流程、操作保護、登出與回歸驗證。

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

目前版本不需要上述功能。

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
7. 已知限制
8. 建議下一階段
```

但最重要的是：

**檔案與程式本身必須真的已經建立並 Build，不要只描述應該怎麼做。**

---

# 37. 登入介面與使用門檻（2026-10-01 新增）

## 37.1 目標與已確認範圍

使用者要求程式必須登入後才能開啟使用，並已確認：**先使用本機帳號與密碼，之後再接正式認證。**

第一階段完成獨立登入視窗、本機帳密驗證、登入狀態、主視窗開啟門檻及登出；來源解析、分析、報告與轉譯流程維持原有架構。所有啟動方式（開發執行、建置產物、單檔 EXE）都須使用相同登入流程。

帳號均為本工具的本機帳號，不等同 Windows 登入帳號。有效且啟用的帳號具有相同的工具使用資格；第一階段不設角色矩陣、自助註冊、忘記密碼、記住密碼、自動登入或雲端帳號同步。

設計選項與取捨：

| 方式 | 優點 | 限制與定位 |
| --- | --- | --- |
| 本機帳號與密碼（已選定） | 不依賴網路或外部服務，可先完成登入功能 | 每台電腦獨立管理帳號，沒有集中停權及跨電腦同步 |
| 既有登入 API | 可沿用正式帳號與集中管理 | 後續須取得實際 API 契約、連線及授權規則 |
| 公司 AD／Microsoft Entra ID | 可沿用公司身分來源 | 後續須確認實際目錄環境、應用程式註冊與登入方式 |

本機登入控制正常程式流程；使用者若有權修改 EXE 或帳號檔案，仍可能改寫本機控制。這個方案不宣稱提供防破解、檔案加密或集中授權。若將來需要可靠的遠端授權，應由正式認證來源及受保護服務執行授權檢查。

## 37.2 使用者流程與畫面

```text
啟動 EXE
  -> 讀取本機認證設定與帳號檔
  -> 顯示 LoginWindow
  -> 輸入帳號與密碼，按「登入」
  -> 驗證帳密與帳號啟用狀態
     -> 成功：建立登入狀態，建立並顯示 MainWindow
     -> 失敗：留在登入畫面，清除密碼並顯示錯誤
  -> 使用分析工具
  -> 登出：清除登入狀態，關閉主視窗，返回新的登入視窗
  -> 關閉主視窗：清除登入狀態並結束程式
```

登入視窗使用現有淺色、藍色 accent 與按鈕樣式，顯示程式名稱「程式移植分析儀 - C#、4GL」、帳號欄位、PasswordBox、錯誤／狀態區、「登入」及「取消」按鈕。建議視窗約 440 × 360 DIP，支援系統縮放及鍵盤操作。

- 初始焦點在帳號；Tab 依序移動；Enter 提交；Esc、「取消」或視窗關閉鈕皆結束程式。
- 帳號空白或密碼空白時不送出驗證，顯示欄位提示。
- 密碼以遮罩顯示，不提供持久化的密碼綁定或記住密碼功能。
- 驗證期間顯示「登入中…」，停用重複提交及帳密編輯；取消操作仍可用，UI 不可 Freeze。
- 成功後主畫面顯示目前使用者的顯示名稱，提供「登出」；不在畫面顯示認證內部資訊。
- 再次啟動或登出後必須重新輸入帳密，不恢復前次登入狀態。

## 37.3 本機帳號建立與儲存

### 帳號檔案

正式執行時帳號檔固定存放於：

```text
%ProgramData%/ProgramMigrationAnalyzer/auth/users.json
```

以 Windows SpecialFolder.CommonApplicationData 解析，不依賴 EXE 所在目錄或目前工作目錄。帳號檔是每台電腦的部署資料，不打包進單檔 EXE，不寫進 source、samples、output 或版本控制。

檔案根節點包含 `schemaVersion`（第一階段為 1）與 `users`。每筆帳號包含：

| 欄位 | 用途 |
| --- | --- |
| userId | 建帳時產生的唯一 GUID，重設密碼時不變 |
| username | 正規化後的帳號名稱 |
| displayName | 主畫面顯示名稱 |
| isEnabled | 是否允許登入本工具 |
| passwordAlgorithm | 固定為 PBKDF2-HMAC-SHA256 |
| iterations | 此帳號的密碼雜湊工作因子 |
| salt | Base64 編碼的隨機 salt |
| passwordHash | Base64 編碼的密碼雜湊 |

帳號名稱為 3～64 個 ASCII 字元，允許英文字母、數字、點、底線及連字號。建立與登入皆先 Trim，再以 invariant 小寫正規化，禁止重複。密碼不 Trim、不轉大小寫、不截斷。新建／重設密碼為 8～128 個 Unicode scalar values，允許 `@`、`!`、`#` 等特殊符號、空白及 Unicode，不強制特定符號組合。最低長度依 2026-10-01 使用者續作指示由 15 改為 8。

### 首次部署與帳號維護

提供 EXE 的獨立 `--configure-local-account` 管理模式，開啟小型帳號設定視窗，讓部署管理者建立帳號、重設密碼或啟用／停用指定帳號；這不是一般登入畫面的註冊入口，也不是完整帳號管理後台。

- 必須檢查目前 Windows 程序已使用提升權限的管理者身分執行；一般權限執行只提示需由管理者設定，不自動提升權限。
- 管理視窗輸入帳號、顯示名稱及兩次遮罩密碼；建立或重設密碼必須兩次一致。僅切換啟用狀態不必重設密碼。
- 密碼不得透過命令列參數、環境變數、README 或腳本傳入；不可內建 `admin/admin` 或任何共用預設密碼。
- 首次設定由此模式建立資料夾與帳號檔，限制 Windows ACL：Administrators 與 SYSTEM 可寫入，一般使用者只能讀取。建立及更新檔案時保留這個 ACL，不允許一般使用者變更帳號設定。
- 更新採相同目錄內的暫存檔及原子替換，避免中斷導致半份 JSON；暫存內容也只包含雜湊，不包含明文密碼。
- 管理模式結束後直接結束程序，不建立分析主視窗、不產生登入狀態；管理者若要使用分析功能仍須正常啟動並登入。
- 帳號檔不存在、損壞、schema 不支援、資料不合法或 ACL 不符合要求時，一般啟動顯示「本機登入尚未正確設定，請聯絡管理者」，禁止登入；不可自動建帳、改用預設密碼或放行。

管理模式及 ProgramData 寫入是後續登入實作的部署要求，本次規格更新不建立實際帳號，也不修改 Windows ACL。

### 密碼處理

優先使用 .NET 內建 `Rfc2898DeriveBytes.Pbkdf2`，不新增僅為雜湊而使用的第三方套件。每次建立／重設密碼使用 `RandomNumberGenerator` 產生至少 16 bytes 的新 salt，使用 SHA-256、600,000 次 iterations 與 32 bytes 雜湊輸出。

驗證讀取帳號記錄中的參數重新計算，使用 `CryptographicOperations.FixedTimeEquals` 比對固定長度結果；不以明文、可逆加密、MD5、單次 SHA-256 或一般字串相等比較取代。拒絕未知演算法、錯誤長度、無效 Base64 及不支援的工作因子；第一階段支援的 iterations 為 600,000～2,000,000，避免檔案遭修改後引發無界計算。

PasswordBox 只在送出當下提供密碼給驗證流程，不存於可觀察的 ViewModel 屬性。可用短生命週期的 SecureString／受控 credential wrapper 交付；需要轉換為文字或 byte buffer 時限定生命週期，finally 中清除可清除的 buffer、解除引用及清空 PasswordBox，不宣稱能抹除所有 managed string 的記憶體副本。

雜湊計算須在背景工作中執行。連續 5 次失敗後，此程序的登入提交暫停 30 秒並顯示剩餘秒數；驗證成功重設計數。這是程序內的節流，重啟會重設，不宣稱提供永久鎖定或跨程序防暴力破解。

## 37.4 元件責任與認證介面

沿用現有專案與 constructor injection，不新建後端服務，也不為登入導入完整 DI framework。

| 所在專案 | 元件 | 責任 |
| --- | --- | --- |
| App | LoginWindow、LoginViewModel | 輸入、欄位驗證、登入命令、狀態與提示 |
| App | ApplicationSessionCoordinator | 啟動、視窗切換、成功登入後建立工作區、登出及結束 |
| App | 本機帳號設定視窗 | 獨立管理模式的互動輸入，不開啟分析工具 |
| Core | IAuthenticationService、LoginRequest、AuthenticationResult | 定義認證請求、成功或失敗結果，不相依 WPF 或 JSON |
| Core | AuthenticatedUser、IUserSession | 提供已驗證身分及唯讀登入狀態 |
| Infrastructure | LocalAuthenticationService、LocalAccountStore | 讀取帳號、驗證啟用狀態、雜湊比對及格式／ACL 檢查 |

`IAuthenticationService.AuthenticateAsync(LoginRequest, CancellationToken)` 回傳 `AuthenticationResult`；請求支援 credentials 或 interactive 類型，第一階段只允許 credentials。密碼載體可釋放，不自行記錄，也不得加入自動產生的 ToString 輸出。

結果包含成功與否、成功時的 `AuthenticatedUser`（UserId、Username、DisplayName、Provider）及可安全顯示的失敗分類。只有有效、啟用且密碼相符的本機帳號可回傳成功；不得以輸入非空、檔案存在或任意 boolean 代替驗證。

`IUserSession` 對使用端只暴露目前使用者與登入狀態；建立／清除狀態由協調器持有，不讓 ViewModel 或任意設定直接把 IsAuthenticated 設為 true。第一階段登入狀態只保存在程序記憶體，持續到登出或程序結束；無 refresh token、磁碟 session 或 idle timeout。

每次登入重新讀取帳號檔；重設密碼或停用帳號於下一次登入生效。第一階段不監控檔案變更來強制撤銷已登入的程序；需要立即停權時須結束現有程序，日後集中認證再處理 session 撤銷。

## 37.5 WPF 啟動與視窗生命週期

現有 `App.OnStartup` 直接建立 MainViewModel 及 MainWindow，須改為先交由協調器顯示登入視窗；移除把「MainWindow 已存在」視為放行條件的流程。`App.xaml` 不得以 StartupUri 直接開啟主視窗。

採用 `ShutdownMode.OnExplicitShutdown`，由協調器統一管理退出：

1. 啟動只建立登入所需元件；不建立 MainWindow、MainViewModel、OutputWriter 或啟動來源載入。
2. 登入成功後先確認登入視窗仍有效且請求未取消，再建立 session 與工作區。
3. 設定 Application.MainWindow、顯示主視窗，然後關閉登入視窗。這次關閉不得誤判為使用者取消。
4. 若主視窗初始化失敗，清除 session 並顯示可重試的登入錯誤，不留下半開啟的工作區。
5. 登入中按取消或關閉視窗時取消請求、清除敏感輸入並呼叫 Application.Shutdown；較晚完成的驗證結果不可再開啟主視窗。
6. 正常關閉主視窗時清除 session、釋放 WebView2 及事件訂閱並呼叫 Shutdown，確保沒有殘留程序。
7. 登出時以明確的切換狀態區分「返回登入」與「結束程式」，不得因主視窗 Closed 事件而結束新的登入畫面。

本機雜湊計算可能無法中途停止；即使背景計算較晚結束，取消結果仍必須被忽略並在 finally 清理，不能把計算結束視為再次登入。

## 37.6 操作門檻與登出

MainViewModel 使用登入狀態：OpenFile、OpenFolder、Analyze、Translate、SaveMarkdown、OpenOutputFolder 的 CanExecute 均須包含已登入條件，命令實際執行入口也須再次檢查。不能只停用按鈕或只保護 MainWindow.Show。

維持現有的「忙碌時停用操作」行為；第一階段分析、轉譯、載入或儲存工作執行中停用登出，提示等待工作完成，避免登出後仍寫入或更新舊工作區。一般關閉程式仍走資源清理與退出流程。

成功登出時：

- 立即讓受保護命令不可執行並清除登入狀態。
- 釋放主視窗、ViewModel 事件及 WebView2，清除記憶體中的文件、來源預覽、分析結果、Markdown、轉譯內容與 Log。
- 已產生在 output 的正式檔案保留；不因登出刪除使用者資料。登入不改變既有輸出檔的 Windows 存取權限。
- 回到全新的登入視窗；下次登入建立全新的工作區，不自動恢復前一位使用者的內容。

Parser、Analyzer 與 Translator 保持可獨立測試的元件，不在每個模型或演算法內加入帳號邏輯。桌面操作入口必須受保護，不能宣稱所有本機 library 呼叫都由登入控管。

## 37.7 錯誤與記錄

| 情況 | 畫面行為 | 狀態 |
| --- | --- | --- |
| 空白帳號／密碼 | 顯示欄位提示，不驗證 | 未登入 |
| 密碼錯誤、帳號不存在或停用 | 統一顯示「帳號或密碼不正確，或帳號無法使用」 | 未登入，清除密碼 |
| 連續失敗達節流條件 | 顯示稍後再試及倒數，不提交驗證 | 未登入 |
| 設定缺漏、檔案損壞或 ACL 不安全 | 提示聯絡管理者，不自動修復或放行 | 未登入 |
| 取消或關閉登入視窗 | 清理並結束程序 | 無 session |
| 主視窗初始化失敗 | 留在／返回登入畫面，提供重試 | 清除 session |
| 後續正式認證服務逾時／連線失敗 | 友善提示並允許重試，禁止切回本機認證放行 | 未登入 |

第一階段記錄登入成功、失敗分類、取消及登出至程序內診斷 Log，登入畫面亦有可讀提示；不另外建立長期稽核資料庫。記錄可包含時間及使用者識別碼，但不可包含密碼、salt、passwordHash、credential wrapper、token 或完整帳號檔內容。

## 37.8 接正式認證的擴充方式

協調器及主畫面只依賴認證介面與登入狀態。日後加入 API 或公司身分 adapter 時，於 composition root 明確選擇單一 provider；正式 provider 驗證失敗、設定缺漏或離線時，不自動 fallback 到本機帳號。

登入畫面依 provider 能力選擇帳密欄位或「使用公司帳號登入」按鈕。如採 Microsoft Entra ID，使用官方認證 library 與 desktop public client 適用的 authorization code + PKCE／OIDC 流程，不在 WPF 中收集公司密碼，也不把 client secret 放進 EXE。

正式串接前須另行取得認證來源、API 或租戶設定、工具使用資格規則、session 有效期、登出及續期契約。若正式來源支援 session 到期，須再設計到期時的操作阻擋及執行中工作的處理；不得沿用本機「直到程序結束」而忽略正式有效期。

## 37.9 登入專項驗收

| 編號 | 情境 | 預期結果 |
| --- | --- | --- |
| L1 | 正常啟動，尚未登入 | 只有登入視窗，未建立主工作區，未載入來源或建立 output |
| L2 | 啟用帳號＋正確密碼 | 只開啟一個主視窗，顯示正確使用者，原功能可用 |
| L3 | 錯誤密碼／未知帳號／停用帳號 | 統一失敗提示，主視窗不存在，密碼清空 |
| L4 | 空白輸入、快速重複 Enter／點擊 | 空白不提交，驗證中只有一次請求，不產生重複視窗 |
| L5 | 驗證完成前取消或關閉登入視窗 | 程序退出，晚到成功結果不建立主視窗 |
| L6 | 帳號檔缺失／損壞／不合法／ACL 不符 | 不 Crash，不自動建帳或放行，提示管理者設定 |
| L7 | 正常登出並以同／不同帳號登入 | 清除 session 與舊工作區，重新驗證，已輸出檔保留 |
| L8 | 分析／轉譯執行中按登出 | 登出停用且有提示，工作完成後才可登出 |
| L9 | 未登入時直接呼叫受保護命令 | 不開對話框、不讀來源、不分析、不轉譯、不儲存輸出 |
| L10 | 關閉主視窗／登入視窗切換 | 正常關閉可退出，登入／登出切換不誤退出也不留殘留視窗 |
| L11 | 管理模式及檔案檢查 | 一般權限無法維護帳號；管理者可建帳／重設／停用，正確 ACL，無明文密碼或共用預設帳密 |
| L12 | 同密碼兩帳號及重設密碼 | salt／hash 不同；舊密碼失效，新密碼有效，大小寫及前後空白依原樣比對 |
| L13 | 連續 5 次登入失敗 | 暫停提交 30 秒，倒數後可重試，不顯示帳號是否存在 |
| L14 | 重啟已登入的程式／執行發佈 EXE | 重新要求登入，沒有測試用登入捷徑或自動登入 |

自動化檢查使用隔離帳號檔及可替換的驗證／檔案服務，不讀写真實 ProgramData 帳號。測試資料只使用測試專用密碼並即時產生雜湊；存放於當次 `.codex-tmp/YYYY-MM-DD_<task-name>/`，不提交帳號資料。

既有 WpfChecks 目前直接建立 MainWindow 並呼叫 app.Run(window)；登入實作時須改以顯式的測試認證服務建立已驗證 session，保留功能檢查，並另外經正式啟動路徑測試 L1～L10。不得把「MainWindow 已存在」、環境變數或啟動參數當作正式程式的免登入開關。

完成登入後執行 Solution build、既有 Phase2Checks／RegressionChecks／WpfChecks 及新增登入檢查；以發佈 EXE 實際驗證啟動、取消、登入、登出與退出。真實 ProgramData／ACL 部署驗證須另取得該電腦管理者的部署授權，不以測試為由修改使用者實際帳號。

## 37.10 後續實作順序

1. 定義認證契約、帳號格式、唯讀 session 與單一 provider 選擇，建立有效／失敗／取消測試。
2. 實作本機密碼雜湊、帳號檔驗證及獨立管理模式，確認首次部署能實際建立可登入帳號。
3. 實作 LoginWindow／LoginViewModel、欄位驗證、背景驗證、節流及取消。
4. 改造 App.OnStartup 與協調器，完成登入成功才建立主工作區及視窗生命週期。
5. 加入操作命令的入口檢查、目前使用者顯示與登出清理。
6. 更新既有測試、完成 L1～L14 與分析／轉譯回歸，更新 README 的帳號部署、登入、登出及已知限制。

本次僅更新規格；上述順序供後續實作使用，不自動安裝套件、建帳、提交、推送或接入外部認證。

## 37.11 技術參考

- [Microsoft：WPF Application.ShutdownMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.application.shutdownmode?view=windowsdesktop-10.0)
- [Microsoft：Rfc2898DeriveBytes](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes?view=net-10.0)
- [OWASP：Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html)
- [Microsoft：Authorization code flow、PKCE 與 OIDC](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow)
