# AI Usage Dashboard — 系統規格

- 版本：`2026-05-16`
- 依據：目前程式碼實作
- 後端：ASP.NET Core 8.0
- 前端：靜態 React UMD + Babel standalone
- 資料庫：MongoDB
- C# root namespace：`AI_Usage_Dashboard.*`

本文件是專案的技術規格與維護契約。README 面向使用者與開源讀者；本文件面向後續開發者，說明資料模型、API contracts、同步流程、重要限制與不可破壞的架構原則。

---

## 1. 產品範圍

AI Usage Dashboard 整合 OpenAI Admin API 與 Azure OpenAI 的用量、成本、模型活動與模型下架風險資料，提供單一 Web UI 與 `/v1` API。

### 1.1 已上線前端頁面

前端入口為 [index.html](./AI_Usage_Dashboard/wwwroot/index.html)，實際頁面切換由 React state `page` 控制，沒有 router。

| Page state | 檔案 | 說明 |
|---|---|---|
| `overview` | `overview.jsx` | KPI、成本/請求/tokens 趨勢、Top models、capability breakdown |
| `deprecated` | `deprecated-models.jsx` | 模型下架風險、urgency summary、受影響 projects |
| `usage` | `usage-detail.jsx` | 用量明細，支援 filter、group、sort、pagination |
| `cost` | `cost-report.jsx` | 成本 breakdown 與 stacked trend |
| `azure` | `azure-page.jsx` | Azure accounts、deployments、model usage raw browser |
| `docs` | `docs-page.jsx` | 內嵌架構說明，可由 tweaks panel 進入，不在 sidebar 主選單 |

`budget-alert.jsx` 仍存在，但目前沒有在 `index.html` 載入，也沒有接到 `App.pageContent`。

### 1.2 後端功能面

| 功能 | 狀態 |
|---|---|
| OpenAI usage / costs raw sync | 已實作 |
| OpenAI org / user / API key catalog raw sync | 已實作 |
| Azure subscription / account / deployment / usage / metrics / cost raw sync | 已實作 |
| Usage / cost read-side aggregation | 已實作 |
| Model deprecation catalog | 已實作，DB-backed |
| Budget / alert API | 已實作，前端未接主流程 |
| CSV export | 已實作，非同步 job |
| Maintenance endpoints | 已實作 |
| AuthN / AuthZ | 未實作 |
| Rate limiting / audit log | 未實作 |

---

## 2. 技術棧與執行模型

### 2.1 後端

| 項目 | 實作 |
|---|---|
| Framework | ASP.NET Core 8.0 (`net8.0`) |
| JSON | `System.Text.Json`，camelCase |
| MongoDB | `MongoDB.Driver` 2.28.0 |
| Azure SDK | `Azure.Identity` 1.21.0 |
| Swagger | `Swashbuckle.AspNetCore`，只在 Development 啟用 |
| Background services | `DataFetchWorker`、`ExportJobService`、`MongoLoggerProvider` |
| Tests | xUnit，目前主要為 `DateRangeHelperTests` |

### 2.2 前端

| 項目 | 實作 |
|---|---|
| React | 18.3.1 UMD from CDN |
| JSX compiler | Babel standalone 7.29.0 from CDN |
| Modules | 無 ES modules；各 `.jsx` 透過 `Object.assign(window, ...)` 暴露元件 |
| API client | `wwwroot/api.js` 的 `window.API` |
| i18n | `wwwroot/i18n.js`，`zh` / `en` |
| Build step | 無。修改 `wwwroot/*` 後重新整理瀏覽器 |

### 2.3 啟動流程

[Program.cs](./AI_Usage_Dashboard/Program.cs) 會：

1. 註冊 controllers、CORS、MongoDbContext、OpenAI/Azure clients、sync services、read-side services。
2. 註冊 `ExportJobService` 與 `DataFetchWorker` hosted services。
3. 註冊 `MongoLoggerProvider`，只持久化 Warning+ logs。
4. 啟動前呼叫 `MongoDbContext.EnsureIndexesAsync()`。
5. 啟動前呼叫 `DeprecationCatalogService.EnsureSeedAsync()`。
6. Development 環境啟用 Swagger。
7. 服務 `wwwroot` 靜態前端並映射 controllers。

---

## 3. 架構原則

下列原則是專案維護契約。新增功能時應優先遵守。

1. **Worker 只做 raw sync**
   - `DataFetchWorker` 與 `*RawSync` services 只讀 API response，補少量 upsert key / sync dimensions，然後寫入 `*_raw` collections。
   - 不在 worker 做商業聚合、label 轉換、成本分攤或 derived collection。

2. **Raw collections 是 read-side 來源**
   - Dropdown、project/user/API key 名稱、models、capabilities 都從 raw collections 讀。
   - 不新增硬編碼 C# dictionary 作為資料真相源。

3. **Frontend 只渲染**
   - 前端 state 存 backend enum，例如 `source='azure'`、`period='7d'`、`dateRange='__custom__|S|E'`。
   - 前端不做成本加總、token 聚合、enum 反推、資料 join。

4. **Aggregation 優先在 MongoDB**
   - `$match`、`$group`、`$switch`、`$regexMatch` 等 aggregation pipeline 是主要 read-side 計算方式。
   - C# 後處理只用於小結果集 enrichment，例如 name lookup、token-share cost allocation。

5. **Secrets 不進 repo**
   - `appsettings.json` 只留安全預設。
   - 真實 OpenAI Admin Key、MongoDB connection string、Azure client secret 必須透過環境變數、User Secrets、Key Vault 或部署平台 secret manager 注入。

---

## 4. Configuration

設定檔範本：[appsettings.example.json](./AI_Usage_Dashboard/appsettings.example.json)。

| Key | 預設 / 行為 |
|---|---|
| `OpenAI:AdminKey` | 必填於實際執行環境；用於 OpenAI Admin API Bearer token |
| `OpenAI:BaseUrl` | `https://api.openai.com/v1/` |
| `OpenAI:OrganizationId` | 可選；若設定，會送 `OpenAI-Organization` header，catalog sync 也會只 upsert 該 org |
| `MongoDB:ConnectionString` | `mongodb://localhost:27017` |
| `MongoDB:Database` | `AI_UsageDashboard` |
| `FetchWorker:IntervalMinutes` | 主 worker loop 間隔，預設 30 |
| `FetchWorker:HistoryDays` | OpenAI 首次無 checkpoint 時回填天數，預設依 worker 設定 |
| `CatalogSync:IntervalMinutes` | OpenAI catalog sync 最短間隔 |
| `AzureCost:TenantId/ClientId/ClientSecret` | 三者皆有值時用 `ClientSecretCredential` |
| `AzureCost:*` 三者皆空 | 使用 `DefaultAzureCredential` |
| `AzureSnapshot:IntervalMinutes` | Azure snapshot sync 最短間隔 |
| `AzureSnapshot:UsageWindowDays` | Azure 首次無 checkpoint 時回填天數 |
| `Export:Directory` | CSV 輸出目錄，預設 `wwwroot/exports` |

目前 CORS 預設允許：

- `http://0.0.0.0:56176`
- `https://0.0.0.0:56175`

正式部署時應改為明確 trusted origins。

---

## 5. MongoDB 資料模型

定義位置：[MongoDbContext.cs](./AI_Usage_Dashboard/Data/MongoDbContext.cs)。

### 5.1 Raw collections

Raw collections 以 `BsonDocument` 儲存 provider responses 與必要 sync dimensions。

| Collection | 寫入來源 | Unique / upsert key |
|---|---|---|
| `openai_usage_raw` | `/organization/usage/{endpoint}` bucket results | `(endpoint, date, projectId, model, userId, apiKeyId, batch)` |
| `openai_costs_raw` | `/organization/costs` bucket results | `(date, projectId, lineItem)` |
| `openai_orgs_raw` | `/organizations` | `_id = id` |
| `openai_users_raw` | `/organization/users` | `_id = id` |
| `openai_api_keys_raw` | `/organization/admin_api_keys` 與 `/organization/projects/{pid}/api_keys` | `_id = id` |
| `azure_subscriptions_raw` | ARM `/subscriptions` | `_id = subscriptionId` |
| `azure_locations_raw` | ARM `/subscriptions/{id}/locations` | `(subscriptionId, name)`，以 replace filter upsert |
| `azure_accounts_raw` | ARM Cognitive Services accounts | `_id = resource id` |
| `azure_deployments_raw` | ARM deployments | `_id = deployment resource id` |
| `azure_usages_raw` | ARM account usages | `(accountId, metricName)` |
| `azure_metric_defs_raw` | Azure Monitor metricDefinitions | `(resourceId, metricName)`，以 replace filter upsert |
| `azure_metrics_raw` | Azure Monitor metrics daily points | `(resourceId, metricName, deploymentName, modelName, modelVersion, region, dateUtc)` |
| `azure_cost_raw` | Azure Cost Management Query rows | `(subscriptionId, resourceId, resourceGroup, resourceLocation, meterCategory, meterSubCategory, meter, serviceName, usageDate)` |

### 5.2 Typed metadata collections

| Collection | POCO | 用途 |
|---|---|---|
| `budgets` | `Budget` | project monthly budget 與目前 spend 狀態 |
| `alert_events` | `AlertEvent` | 預算跨閾值事件 |
| `export_jobs` | `ExportJob` | CSV export job 狀態 |
| `fetch_checkpoints` | `FetchCheckpoint` | sync checkpoint |
| `deprecation_catalog` | `DeprecationCatalogEntry` | 模型下架目錄 |
| `system_logs` | `BsonDocument` | Warning+ logs |

### 5.3 Indexes

`EnsureIndexesAsync()` 會建立：

| Collection | Index |
|---|---|
| `openai_usage_raw` | unique `openai_usage_unique`；query `(date desc, projectId, model)` |
| `openai_costs_raw` | unique `openai_costs_unique`；query `(date desc, projectId)` |
| `azure_cost_raw` | unique `azure_cost_unique`；query `(usageDate desc, subscriptionId)` |
| `azure_metrics_raw` | unique `azure_metrics_unique`；query `(dateUtc desc, subscriptionId, accountName)` |
| `azure_accounts_raw` | `_id` |
| `azure_deployments_raw` | `(subscriptionId, accountName)` |
| `azure_usages_raw` | unique `(accountId, metricName)` |
| `alert_events` | `(timestamp desc)`、`(projectId, timestamp desc)` |
| `deprecation_catalog` | `(isEnabled, shutdownDate)` |
| `system_logs` | `timestamp` TTL 30 days、`(level, timestamp desc)`、`(category, timestamp desc)` |

---

## 6. 同步流程

### 6.1 DataFetchWorker

`DataFetchWorker` 在啟動時先跑一次 cycle，之後每 `FetchWorker:IntervalMinutes` 分鐘執行一次。

Sync 順序：

| Step | Label / checkpoint | Service | 行為 |
|---|---|---|---|
| 1 | `openai_usage` | `OpenAiUsageRawSync` | 同步 OpenAI usage endpoints |
| 2 | `openai_costs` | `OpenAiCostsRawSync` | 同步 OpenAI costs |
| 3 | `openai_catalog` | `OpenAiCatalogRawSync` | 同步 orgs、users、API keys，受 `CatalogSync:IntervalMinutes` 控制 |
| 4 | `azure_snapshot` | `AzureSnapshotOrchestrator` | 同步 Azure metadata、metrics、cost，受 `AzureSnapshot:IntervalMinutes` 控制 |

每個 sync step 都包在 `TryAsync`，單一來源失敗只記 Warning log，不會中斷其他來源。`OperationCanceledException` 會正常傳出。

Checkpoint window：

- 無 checkpoint：從目前 UTC 往前回填設定天數。
- 有 checkpoint：從 checkpoint 減 2 天開始重抓，提供重疊容錯。
- 完成後 upsert `fetch_checkpoints`。

### 6.2 OpenAI usage sync

`OpenAiUsageRawSync` 會同步下列 endpoints：

- `completions`
- `responses`
- `embeddings`
- `images`
- `audio_speeches`
- `audio_transcriptions`
- `moderations`
- `vector_stores`
- `code_interpreter_sessions`

重要行為：

- 依月份切段，再以 7 天 chunk 呼叫 API。
- `bucket_width=1d`。
- 一般 endpoints 使用 `group_by=project_id,model,user_id,api_key_id`。
- `vector_stores` 與 `code_interpreter_sessions` 只使用 `group_by=project_id`。
- `batch` 是 filter param，不是 `group_by`。
- upsert 時補入 `endpoint`、`date`、`projectId`、`model`、`userId`、`apiKeyId`、`batch`。

### 6.3 OpenAI costs sync

`OpenAiCostsRawSync`：

- 呼叫 `/organization/costs`。
- `group_by=project_id&group_by=line_item`。
- 以 7 天 chunk 同步。
- 如果 request `end` 不是 UTC midnight，會 ceil 到下一個 UTC midnight，避免漏掉今日 partial bucket。
- upsert 時補入 `date`、`projectId`、`lineItem`。

OpenAI Costs API 不提供 `group_by=model`。Read-side 的 model cost 是由 usage token-share 分攤而來。

### 6.4 OpenAI catalog sync

`OpenAiCatalogRawSync`：

- `/organizations` 寫入 `openai_orgs_raw`。
- `/organization/users` 寫入 `openai_users_raw`。
- `/organization/admin_api_keys` 寫入 `openai_api_keys_raw`，補 `kind=admin`。
- 從 `openai_orgs_raw.projects.data[]` 列出 projects，再呼叫 `/organization/projects/{pid}/api_keys`，補 `kind=project` 與 `projectId`。

若 `OpenAI:OrganizationId` 有設定：

- `/organizations` 回應只 upsert 該 org。
- 後續 project API key fan-out 只會基於已保留的 org projects。

### 6.5 Azure snapshot sync

`AzureSnapshotOrchestrator` 對每個 active subscription 執行：

1. `AzureSubscriptionsRawSync`
2. `AzureLocationsRawSync`
3. `AzureAccountsRawSync`
4. 對每個 Cognitive Services account：
   - `AzureDeploymentsRawSync`
   - `AzureUsagesRawSync`
   - `AzureMetricsRawSync`
5. `AzureCostRawSync`

Active subscription 定義：

- state 空白、`Enabled` 或 `Warned`。

### 6.6 Azure metrics sync

`AzureMetricsRawSync`：

- 先同步 metric definitions 到 `azure_metric_defs_raw`。
- Azure Monitor `metricnames` 每次最多 20 個。
- 依 metric 是否支援 `ModelName` / `ModelDeploymentName` 分組，決定 `$filter`：
  - 兩者皆支援：`ModelDeploymentName eq '*' and ModelName eq '*'`
  - 只支援 deployment：`ModelDeploymentName eq '*'`
  - 只支援 model：`ModelName eq '*'`
  - 都不支援：不送 filter
- 每個 daily data point 寫入 `azure_metrics_raw`。
- `metadatavalues[].name.value` 以小寫讀取，例如 `modeldeploymentname`。
- 維度值 `"__Empty"` 會正規化為空字串。

### 6.7 Azure cost sync

`AzureCostRawSync`：

- 呼叫 Cost Management Query API `2023-03-01`。
- `type=Usage`、`timeframe=Custom`、`granularity=Daily`。
- aggregation 使用 `CostUSD` sum。
- grouping dimensions：
  - `ResourceId`
  - `ResourceGroup`
  - `ResourceLocation`
  - `MeterCategory`
  - `MeterSubCategory`
  - `Meter`
  - `ServiceName`
- API 回傳的 `usageDate` 是 numeric `yyyyMMdd`，會以 number 寫入 MongoDB。
- 另補 `subscriptionId`、`subscriptionName`、`updatedAtUtc`、`costCurrency="USD"`。

注意：API 回傳的 `currency` 欄位是 subscription billing currency，不代表 `costUSD` 的幣別。系統以 `costCurrency="USD"` 作為 costUSD 的語義標記。

---

## 7. Read-side aggregation

### 7.1 日期區間

`DateRangeHelper.ResolvePeriod(period, startDate, endDate)` 回傳 `[from, to)` UTC date window。

支援：

| period | 行為 |
|---|---|
| `today` | 今日 UTC 00:00 到明日 UTC 00:00 |
| `7d` | 今日往前 7 天到明日 |
| `30d` | 今日往前 30 天到明日 |
| `MTD` | 本月 1 日到明日 |
| `custom` | `startDate.Date` 到 `endDate.Date + 1 day` |
| unknown | fallback 到 `MTD` |

API 邊界上的 `endDate` 是 inclusive；內部一律轉成 exclusive upper bound。

### 7.2 UsageReadService — OpenAI

`AggregateOpenAiUsageAsync`：

- `$match` by date、projectId、model、capability。
- capability 透過 endpoint `$switch` 轉換。
- `groupBy` 支援：
  - 空字串：natural key `(date, projectId, model, userId, apiKeyId)`
  - `project`
  - `user`
  - `apikey`
  - `model`
  - `servicetier`
  - `date`
- input tokens = `input_tokens + input_audio_tokens`。
- output tokens = `output_tokens + output_audio_tokens`。
- requests = `num_model_requests` sum。
- C# 後處理：
  - `NameLookupService` 補 project/user/API key name。
  - `EnrichOpenAiCostsAsync` 將 `openai_costs_raw` 的 `(projectId, date)` cost 依 token share 分配到 usage rows。

### 7.3 UsageReadService — Azure

`AggregateAzureUsageAsync`：

- 從 `azure_metrics_raw` 聚合。
- `$match` by `dateUtc`、`subscriptionId`、`accountName`、`modelName`。
- group key 為 `(date, subscriptionId, accountName, model)`。
- model fallback：`modelName -> deploymentName -> "azure-openai"`。
- requests regex：`Requests|ModelRequests`。
- input token regex：`InputTokens|PromptTokens|ProcessedPromptTokens|AudioInputTokens`。
- output token regex：`OutputTokens|CompletionTokens|GeneratedTokens|AudioOutputTokens`。
- 聚合後會移除 requests/inputTokens/outputTokens 全為 0 的 rows。
- Azure cost 從 `azure_cost_raw` 依 `(subscriptionId, accountName, usageDate)` 聚合後，按 token share 分配到 usage rows。

### 7.4 NameLookupService

快取 TTL：1 分鐘。

| Method | 資料來源 |
|---|---|
| `GetProjectNamesAsync` | `openai_orgs_raw.projects.data[]` |
| `GetUserNamesAsync` | `openai_users_raw` |
| `GetApiKeyNamesAsync` | `openai_api_keys_raw` |
| `GetSubscriptionNamesAsync` | `azure_subscriptions_raw`，不使用 static cache |

### 7.5 成本歸因

- OpenAI Costs API 無 model 維度，因此 model / usage row cost 是按 token share 分配。
- Azure cost 來自 Cost Management resource / meter / day 維度，usage row cost 同樣按 token share 分配。
- Dashboard 顯示的 model-level cost 是估算歸因，不是 provider 直接回傳的 model-level bill。

---

## 8. API Contracts

所有 API 路徑以 `/v1` 為前綴。Response JSON 使用 camelCase。

### 8.1 Common query conventions

| Query | 說明 |
|---|---|
| `source` | `all` / `openai` / `azure`，未知值 fallback |
| `orgId` | OpenAI org id 或 Azure subscription id，視 source 而定 |
| `projectId` | OpenAI project id 或 Azure account name，視 source 而定 |
| `period` | `today` / `7d` / `30d` / `MTD` / `custom` |
| `startDate` / `endDate` | `custom` 時使用 |

### 8.2 Usage API

Base route：`/v1/usage`

| Method | Path | 說明 |
|---|---|---|
| GET | `/overview` | 回傳 KPI 與上一期 delta |
| GET | `/trend` | 回傳每日 cost / requests / tokens |
| GET | `/records` | 回傳 usage rows，支援 filter / group / sort / pagination |
| GET | `/filters` | 回傳 models 與 capabilities dropdown 候選 |

`/records` 額外 query：

| Query | 說明 |
|---|---|
| `model` | model filter |
| `capability` | OpenAI capability filter |
| `groupBy` | `project` / `user` / `apikey` / `model` / `servicetier` / `date` / 空 |
| `sortBy` | `date` / `project` / `user` / `model` / `capability` / `inputtokens` / `outputtokens` / `requests` / `costusd` |
| `sortDir` | `asc` / `desc` |
| `page` / `pageSize` | pagination |

### 8.3 Cost API

Base route：`/v1/cost`

| Method | Path | 說明 |
|---|---|---|
| GET | `/breakdown` | 成本 breakdown |
| GET | `/trend-stacked` | stacked daily cost trend |

`/breakdown`：

- `groupBy` 支援 `project`、`model`、`capability`、`date`。
- `sortBy` 支援 `cost`、`label`。
- 回傳 `CostBreakdownResponse`，`currency` 固定 `USD`。

`/trend-stacked`：

- `topN` clamp 到 3..20。
- 超過 topN 的 series 合併為 `__other` / `Other`。

### 8.4 Org / Project API

Base route：`/v1`

| Method | Path | 說明 |
|---|---|---|
| GET | `/orgs?source=openai|azure` | OpenAI 回 orgs；Azure 回 subscriptions |
| GET | `/projects?source=openai|azure&orgId=` | OpenAI 回 projects；Azure 回 accounts |

注意：`ProjectsController.NormalizeSource` 只接受 `azure`，其他值視為 `openai`。

### 8.5 Models API

Base route：`/v1/models`

| Method | Path | 說明 |
|---|---|---|
| GET | `/deprecated` | 依 usage rows 與 deprecation catalog 計算下架風險 |
| GET | `/deprecation-catalog` | 讀完整 catalog |
| PUT | `/deprecation-catalog/{model}` | 新增或更新 catalog entry |
| DELETE | `/deprecation-catalog/{model}` | 刪除 catalog entry |
| POST | `/deprecation-catalog/rebuild` | 清空並重建預設 catalog |

Urgency：

| daysUntilShutdown | urgency |
|---|---|
| `<= 0` | `expired` |
| `<= 30` | `critical` |
| `<= 90` | `warning` |
| `> 90` | `upcoming` |

Sort 支援：

- `daysUntilShutdown`
- `modelName`
- `substituteModel`
- `shutdownDate`
- `urgency`
- `totalRequests`
- `lastSeenDate`

### 8.6 Azure API

Base route：`/v1/azure`

| Method | Path | 說明 |
|---|---|---|
| GET | `/status` | raw collection counts 與 checkpoints |
| GET | `/overview` | Azure cost top accounts |
| GET | `/query` | Azure accounts / deployments / modelUsage 分頁查詢 |

`/query` datasets：

| dataset | Source collection | 說明 |
|---|---|---|
| `accounts` | `azure_accounts_raw` | accounts list |
| `deployments` | `azure_deployments_raw` | deployments list，region 由 parent account 補 |
| `modelUsage` | `azure_metrics_raw` | 聚合成 requests/input/output tokens daily rows |

`pageSize` clamp 到 1..200。

### 8.7 Budget / Alert API

| Method | Path | 說明 |
|---|---|---|
| GET | `/v1/budgets` | 回傳 budgets 與 summary |
| PUT | `/v1/budgets/{projectId}` | 更新 existing budget 的 monthlyBudget，找不到回 404 |
| GET | `/v1/alerts` | 回傳 alert events |

Budget alert level：

| pct | level |
|---|---|
| `>= 100` | `critical` |
| `>= 90` | `high` |
| `>= 80` | `warning` |
| `< 80` | `ok` |

只有跨越更高閾值時才新增 `alert_events`。

### 8.8 Export API

Base route：`/v1/export`

| Method | Path | 說明 |
|---|---|---|
| POST | `/` | 建立 export job，回 202 |
| GET | `/{jobId}` | 查 job 狀態 |

Job 狀態：

- `pending`
- `preparing`
- `ready`
- `failed`

`ready` 時 `downloadUrl=/exports/{jobId}.csv`。

### 8.9 Maintenance API

Base route：`/v1/maintenance`

| Method | Path | 說明 |
|---|---|---|
| POST | `/reset-today` | 將 `openai_usage`、`openai_costs`、`azure_snapshot` checkpoint 設為 yesterday UTC |
| POST | `/backfill?months=N` | 將三個 checkpoint 設為 N 個月前，N clamp 1..24 |
| POST | `/full-reset` | 刪除所有 checkpoints |
| GET | `/checkpoints` | 列出 checkpoints |
| GET | `/logs` | 查 `system_logs`，支援 level/category/search/since/page/pageSize |
| GET | `/logs/summary?hours=N` | 依 level/category 聚合 log count，hours clamp 1..720 |

---

## 9. Frontend contract

### 9.1 Global state

`index.html` 中 `App` 持有：

| State | 說明 |
|---|---|
| `page` | `overview` / `deprecated` / `usage` / `cost` / `azure` / `docs` |
| `theme` | `light` / `dark` |
| `collapsed` | sidebar collapsed |
| `lang` | `zh` / `en` |
| `org` | OpenAI org id 或 Azure subscription id |
| `projectId` | OpenAI project id 或 Azure account name |
| `source` | `all` / `openai` / `azure` |
| `dateRange` | backend period enum 或 `__custom__|YYYY-MM-DD|YYYY-MM-DD` |
| `tweaks` | localStorage-backed UI tweaks |
| `textScale` | body zoom scale |
| `orgList` | `/v1/orgs` response |
| `projectMap` | selected org/subscription 下的 projects/accounts |

當 `source === 'all'` 時，前端會清空 `org` 與 `projectId`。

### 9.2 API wrapper

`api.js` 暴露：

- `API.getOverview`
- `API.getTrend`
- `API.getCostBreakdown`
- `API.getCostTrendStacked`
- `API.getUsageRecords`
- `API.getBudgets`
- `API.putBudget`
- `API.getAlerts`
- `API.createExport`
- `API.getExport`
- `API.getProjects`
- `API.getOrgs`
- `API.getUsageFilters`
- `API.getDeprecatedModels`
- `API.getAzureOverview`
- `API.getAzureStatus`
- `API.getAzureQuery`

`dateRangeQuery(period)` 負責把 `__custom__|S|E` 轉成 `{ period:'custom', startDate:S, endDate:E }`。

### 9.3 Static asset model

- 由 ASP.NET Core `UseDefaultFiles()` / `UseStaticFiles()` 直接服務 `wwwroot`。
- `file://` 開啟 `index.html` 時會 redirect 到 `http://localhost:5088/`。
- 前端沒有 npm 或 build pipeline。

---

## 10. Deprecation catalog

`DeprecationCatalogService` 是 DB-backed。

啟動時：

- 若 `deprecation_catalog` 為空，呼叫 `EnsureSeedAsync()` 寫入預設 entries。

`Lookup(rawModel, snapshot)` 流程：

1. exact match。
2. 若 exact match 失敗，使用 `StripSnapshotSuffix` 移除尾端 snapshot suffix 後再查。
3. 仍找不到則視為非 deprecated。

支援移除的 suffix：

- `-YYYY-MM-DD`
- `-MMDD`
- 上述任一形式後面可接 `-preview`

Regex 是 end-anchored，避免把 `gpt-4o-2024-05-13` 誤判為 `gpt-4`。

若某 snapshot 的 shutdown date 比 base family 更早，應在 catalog 中新增 explicit row，讓 exact match 優先命中。

---

## 11. Logging

`MongoLoggerProvider`：

- 實作 `ILoggerProvider`。
- 只處理 `Warning`、`Error`、`Critical`。
- 使用 bounded channel，容量 10,000，滿時 drop oldest。
- 背景批次寫入 `system_logs`，每批最多 256 筆。
- logging 寫入失敗永不往外拋。

`system_logs` schema：

| Field | 說明 |
|---|---|
| `timestamp` | UTC DateTime |
| `level` | `warn` / `error` / `critical` |
| `category` | ILogger category |
| `eventId` | int |
| `message` | formatted message |
| `exception` | optional full exception |
| `exceptionType` | optional exception type |

TTL：30 days。

---

## 12. Export

`ExportJobService`：

- hosted service。
- 使用 unbounded channel 排隊 job id。
- 建立 job 時寫入 `export_jobs`，狀態 `pending`。
- worker 取出後設為 `preparing`。
- 完成後狀態 `ready`，設定 `downloadUrl`。
- 失敗後狀態 `failed`，記錄 `errorMessage`。

CSV：

| Type | Columns |
|---|---|
| `usage` | `Date, Project, User, ApiKey, Model, Capability, InputTokens, OutputTokens, Requests, Cost(USD)` |
| `cost` | `Date, Project, Model, Capability, Cost(USD)` |

`CsvSerializer` 使用 UTF-8 BOM，所有欄位都以 quotes 包起來，內部 quotes 會 double。

---

## 13. 重要實作細節與踩雷

### 13.1 Azure cost `usageDate`

`azure_cost_raw.usageDate` 是 numeric `yyyyMMdd`，例如 `20260516`。所有 `$match` 必須用 number 比較，不可用 string。

### 13.2 Azure cost currency

`costUSD` 是 Azure Cost Management 預先換算的 USD。API row 的 `currency` 是 subscription billing currency，不代表 `costUSD` 幣別。

### 13.3 Azure metric dimensions

Azure Monitor metric dimension name value 是小寫，例如 `modeldeploymentname`。缺值或 `"__Empty"` 會被 sync 正規化為空字串。

### 13.4 Azure Monitor metricnames limit

Azure Monitor `/metrics` 單次 `metricnames` 最多 20 個。不同 metric 支援不同 dimensions，所以需依 dimension support 分組呼叫。

### 13.5 OpenAI Costs API 無 model group_by

OpenAI model-level cost 是 read-side 以 token share 估算，不是 OpenAI API 直接回傳。

### 13.6 OpenAI usage endpoint group_by 差異

`vector_stores` 與 `code_interpreter_sessions` 只支援 `project_id`。送 `model`、`user_id`、`api_key_id` 會失敗。

### 13.7 Raw unique key 必須與 upsert filter 對齊

新增 sync dimension 時，必須同步修改：

1. raw document 補入欄位。
2. `ReplaceOneModel` filter。
3. `MongoDbContext.EnsureIndexesAsync()` unique index。

否則可能出現 duplicate key 或資料覆蓋。

---

## 14. 安全狀態

目前安全狀態：

| 項目 | 狀態 |
|---|---|
| AuthN / AuthZ | 未實作 |
| CORS | 固定 development origins |
| Rate limiting | 未實作 |
| Audit log | 未實作 |
| Secrets in repo | 設定檔已改安全預設；請仍自行掃描並 rotate 曾外洩 secrets |
| Export file protection | 目前由 static files 直接服務 |
| Maintenance endpoint protection | 未實作 |

正式部署前至少應補：

- SSO/JWT + RBAC。
- 嚴格 CORS allowlist。
- Rate limiting。
- Maintenance / budget / catalog / export mutation audit log。
- Secret manager。
- MongoDB network isolation。
- Export 檔案存取控制與保留策略。

---

## 15. 測試現況

目前測試專案：

- `AI_Usage_Dashboard.Tests`
- xUnit
- 主要覆蓋 `DateRangeHelperTests`

建議新增：

- `UsageReadService` aggregation integration tests。
- `DeprecationCatalogService.Lookup` tests。
- controller contract tests。
- Azure `usageDate` numeric range tests。
- export job state transition tests。

---

## 16. 已知限制與技術債

| 項目 | 說明 |
|---|---|
| 無 AuthN/AuthZ | 不可直接公開部署 |
| raw collections 無 TTL | usage/cost/metrics 會持續累積 |
| 測試覆蓋率低 | read-side pipeline 與 controllers 缺 integration tests |
| `budget-alert.jsx` 未接線 | 後端有 Budget/Alert API，但前端主流程未啟用 |
| `docs-page.jsx` 可能落後 | 以本 SPEC 為準 |
| 成本歸因為估算 | model-level cost 使用 token-share allocation |
| `AzureCost:IntervalMinutes` | 保留設定但 Azure cost 實際併入 `AzureSnapshotOrchestrator` |
| CORS 固定開發 origin | production 需調整 |

---

## 17. 變更規格時的同步事項

若修改下列行為，必須同步更新本文件：

- 新增或移除 API endpoint。
- 改變 query parameter、response DTO、日期語義。
- 改變 raw collection schema、upsert key 或 index。
- 改變 worker sync window、checkpoint 行為或 provider endpoint。
- 改變前端 state enum 或 API wrapper contract。
- 改變 deprecation catalog lookup 規則。
- 改變安全模型、CORS、secret management 或 deployment assumptions。
