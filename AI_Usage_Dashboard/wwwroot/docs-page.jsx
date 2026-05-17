// docs-page.jsx — Architecture documentation (reflects actual codebase)

const CODE_BLOCKS = {
  folderStructure: `AI_Usage_Dashboard/
├── Controllers/
│   ├── AlertController.cs        GET /v1/alerts
│   ├── AzureController.cs        GET|POST /v1/azure/*
│   ├── BudgetController.cs       GET|PUT /v1/budgets
│   ├── CostController.cs         GET /v1/cost/breakdown
│   ├── ExportController.cs       POST /v1/export, GET /v1/export/:jobId
│   ├── MaintenanceController.cs  POST /v1/maintenance/backfill
│   ├── ModelsController.cs       GET /v1/models/deprecated
│   ├── ProjectsController.cs     GET /v1/projects, /v1/orgs
│   └── UsageController.cs        GET /v1/usage/*
├── Services/
│   ├── OpenAiUsageService.cs     OpenAI Usage API 抓取、日聚合
│   ├── OpenAiCostService.cs      OpenAI Cost CSV 抓取與解析
│   ├── OpenAiHttpClient.cs       OpenAI Admin API HTTP 封裝
│   ├── OpenAiOrganizationService.cs  Org ID 解析與快取
│   ├── OpenAiCatalogSyncService.cs   模型 catalog + 專案名稱同步
│   ├── UserLookupService.cs      使用者 / API Key 名稱快取
│   ├── AzureCostSyncService.cs   Azure Cost Management API 同步
│   ├── AzureCostReadService.cs   azure_cost_daily 聚合查詢
│   ├── AzureSnapshotSyncService.cs  Azure Monitor 每日 token/request 同步
│   ├── AzureUsageReadService.cs  azure_openai_model_usage → UsageRecord
│   ├── BudgetAlertService.cs     預算計算與告警觸發
│   ├── ExportJobService.cs       CSV 匯出排程
│   └── ModelCatalogService.cs    模型定價與棄用元資料
├── Workers/
│   └── DataFetchWorker.cs        背景排程（每 30 分鐘主循環）
├── Models/
│   ├── DomainModels.cs           實體模型
│   └── ApiModels.cs              API 回應 DTOs
├── Data/
│   └── MongoDbContext.cs         MongoDB 集合定義
├── wwwroot/
│   ├── index.html                入口（Babel in-browser，無建置步驟）
│   ├── api.js                    API 客戶端封裝
│   ├── i18n.js                   多語系字串（EN / ZH-TW）
│   ├── charts.jsx                SVG 圖表（LineChart, BarChart, DonutChart, SparkLine）
│   ├── layout.jsx                Sidebar / Header / Card / Skeleton 等共用元件
│   ├── overview.jsx              總覽頁（KPI + 趨勢 + 成本分布）
│   ├── usage-detail.jsx          用量明細頁（分頁 + 分組 + 排序）
│   ├── cost-report.jsx           成本報表頁（多維度拆解 + CSV 匯出）
│   ├── deprecated-models.jsx     已棄用模型頁（critical / warning / upcoming）
│   ├── azure-page.jsx            Azure 資料頁（帳戶 / 部署 / 模型用量）
│   ├── tweaks-panel.jsx          視覺調整面板（暗色模式 / 主題色 / 大字模式）
│   └── docs-page.jsx             本頁
└── appsettings.json              MongoDB / OpenAI / Azure / 排程設定`,

  domainModels: `// Models/DomainModels.cs（簡化）

public sealed class UsageRecord {
    public DateTime Date       { get; set; }
    public string OrgId        { get; set; }  // OpenAI orgId / Azure subscriptionName
    public string ProjectId    { get; set; }  // OpenAI projectId / Azure accountName
    public string ProjectName  { get; set; }
    public string UserId       { get; set; }
    public string UserName     { get; set; }
    public string ApiKeyId     { get; set; }
    public string ApiKeyName   { get; set; }
    public string Model        { get; set; }
    public string Capability   { get; set; }  // "Chat Completions" / "Azure OpenAI" 等
    public long   InputTokens  { get; set; }
    public long   OutputTokens { get; set; }
    public long   Requests     { get; set; }
    public decimal CostUsd     { get; set; }
    public string ServiceTier  { get; set; }  // default / flex / batch / azure
    public bool   IsDeprecated { get; set; }
    public string ShutdownDate { get; set; }
}

public sealed class AzureCostDaily {
    public DateTime UsageDate        { get; set; }
    public string SubscriptionId     { get; set; }
    public string SubscriptionName   { get; set; }
    public string ResourceGroup      { get; set; }
    public string AccountName        { get; set; }
    public string ResourceId         { get; set; }
    public decimal CostUsd           { get; set; }
    public string MeterCategory      { get; set; }
    public DateTime UpdatedAtUtc     { get; set; }
}

public sealed class Budget {
    public string  ProjectId      { get; set; }  // _id
    public decimal MonthlyBudget  { get; set; }
    public decimal Spent          { get; set; }
    public double  Pct            { get; set; }
    public decimal Remaining      { get; set; }
    public string  Level          { get; set; }  // ok / warning / high / critical
}`,

  mongoCollections: `# MongoDB 集合（Database: AI_UsageDashboard）

## 核心資料
usage_records             日用量記錄（OpenAI + Azure 統一 UsageRecord 格式）
budgets                   專案月度預算設定（_id = projectId）
alert_events              預算告警事件（level / threshold / timestamp）
export_jobs               CSV 匯出任務狀態（pending → completed / failed）
fetch_checkpoints         增量同步游標（source → lastFetchedAt）

## 快取
users_cache               OpenAI 使用者 ID → name / email / role
api_keys_cache            API Key ID → name（遮罩值）
project_catalog           projectId → projectName（OpenAI catalog）
org_catalog               orgId → isDefault

## Azure
azure_cost_daily          Azure Cost Management 每日費用（逐日 upsert）
azure_openai_model_usage  Azure Monitor 每日 token / request 用量（逐日 upsert）
azure_ai_accounts         Azure OpenAI 帳戶清單（ARM API 掃描）
azure_ai_deployments      部署設定（ARM API 掃描）
azure_ai_usage_status     部署用量狀態快照
azure_subscriptions_overview  訂閱摘要
azure_scan_issues         掃描錯誤記錄（level / statusCode / message）
azure_sync_meta           同步時間戳（source → updatedAtUtc）`,

  workerSchedule: `# DataFetchWorker（ASP.NET Core BackgroundService）

主循環間隔：FetchWorker:IntervalMinutes（預設 30 min）

┌──────────────────────────────────────────────────┬──────────────┐
│ 工作項目                                           │ 最小間隔      │
├──────────────────────────────────────────────────┼──────────────┤
│ OpenAI 用量抓取（usage_records upsert）            │ 每次循環       │
│ OpenAI 成本 CSV 抓取（usage_records upsert）       │ 每次循環       │
│ Azure 成本同步（azure_cost_daily upsert）          │ 30 分鐘        │
│ Azure 模型用量快照（azure_openai_model_usage）     │ 60 分鐘        │
│ 模型 Catalog 同步（project_catalog / org_catalog）│ 180 分鐘       │
│ 預算告警重算（budgets / alert_events）             │ 每次循環       │
└──────────────────────────────────────────────────┴──────────────┘

設定項目（appsettings.json）：
  FetchWorker.IntervalMinutes       = 30    主循環間隔（分鐘）
  FetchWorker.HistoryDays           = 90    最大回溯天數
  AzureCost.IntervalMinutes         = 30
  AzureSnapshot.IntervalMinutes     = 60
  AzureSnapshot.UsageWindowDays     = 30   每次同步的天數視窗
  CatalogSync.IntervalMinutes       = 180

手動觸發：
  POST /v1/azure/sync-cost?days=32
  POST /v1/azure/sync-snapshot?days=183   最多 183 天回補
  POST /v1/maintenance/backfill?months=6  重設 OpenAI fetch checkpoints`,

  apiEndpoints: `# REST API（Base: /v1）

## 用量總覽
GET  /v1/usage/overview
     ?dateRange=本月至今|7天|30天|自訂&orgId=&projectId=&source=openai|azure|all
     → { monthlyCost, monthlyCostDelta, totalRequests, totalRequestsDelta,
         inputTokens, outputTokens, avgDailyCost }

GET  /v1/usage/trend
     ?orgId=&projectId=&days=30&source=
     → [{ date, cost, requests, inputTokens, outputTokens }]

GET  /v1/usage/records
     ?orgId=&projectId=&model=&capability=&userId=&apiKeyId=
     &groupBy=none|project|model|capability|user|apiKey
     &sortBy=&sortDir=asc|desc&page=&pageSize=&source=
     → PaginatedResponse<UsageRecord>

## 成本
GET  /v1/cost/breakdown
     ?groupBy=project|model|capability|date&dateRange=&orgId=&projectId=&source=
     → [{ key, cost, pct }]

## 專案 / 組織
GET  /v1/projects?orgId=&source=   → [{ projectId, projectName }]
GET  /v1/orgs?source=              → [{ orgId }]

## 模型
GET  /v1/models/deprecated?projectId=&source=
     → { totalDeprecated, expired, critical, warning, upcoming, models:[...] }

## 預算與告警
GET  /v1/budgets                          → Budget[]
PUT  /v1/budgets/:projectId  { monthlyBudget }  → Budget
GET  /v1/alerts?orgId=&startDate=&endDate=&limit=50  → AlertEvent[]

## 匯出
POST /v1/export  { type: usage|cost, filters }  → { jobId }
GET  /v1/export/:jobId                          → { status, downloadUrl? }

## Azure
GET  /v1/azure/overview?limit=20
     → { source, lastSyncAtUtc, totalRows, topAccounts[], recent[] }
GET  /v1/azure/status
     → { counts: { azure_ai_accounts, azure_ai_deployments,
                   azure_openai_model_usage, azure_cost_daily, ... }, syncMeta }
GET  /v1/azure/query
     ?dataset=accounts|deployments|modelUsage&page=&pageSize=&search=
     → PaginatedResponse<object>
GET  /v1/azure/scan-issues             → 掃描錯誤清單
POST /v1/azure/sync-cost?days=32       → 觸發 Azure 成本同步
POST /v1/azure/sync-snapshot?days=30   → 觸發 Azure 模型用量同步

## 維護
POST /v1/maintenance/backfill?months=6  → 重設 OpenAI fetch checkpoints
GET  /v1/maintenance/checkpoints        → 查看目前 checkpoints`
};

function CodeBlock({ code }) {
  const [copied, setCopied] = React.useState(false);
  function copy() {
    navigator.clipboard.writeText(code).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 1800);
    });
  }
  return (
    <div style={{ position: 'relative', marginBottom: 20 }}>
      <button onClick={copy} style={{
        position: 'absolute', top: 8, right: 8, padding: '3px 8px',
        background: copied ? 'rgba(16,185,129,0.15)' : 'rgba(255,255,255,0.08)',
        border: '1px solid rgba(255,255,255,0.12)', borderRadius: 4,
        color: copied ? '#10B981' : '#9CA3AF', fontSize: 10, cursor: 'pointer', fontWeight: 600, zIndex: 1,
      }}>{copied ? '✓ 已複製' : '複製'}</button>
      <pre style={{
        background: 'var(--code-bg)', border: '1px solid var(--border)', borderRadius: 8,
        padding: '16px 14px', fontSize: 11.5, lineHeight: 1.65, overflowX: 'auto',
        color: 'var(--code-text)', margin: 0, fontFamily: "'JetBrains Mono','Fira Code',monospace",
      }}><code>{code}</code></pre>
    </div>
  );
}

function DocSection({ title, children }) {
  return (
    <div style={{ marginBottom: 28 }}>
      <h2 style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-primary)', margin: '0 0 12px',
        paddingBottom: 8, borderBottom: '1px solid var(--border)', letterSpacing: '-0.2px' }}>
        {title}
      </h2>
      {children}
    </div>
  );
}

function OpenAISnapshot() {
  const lang = React.useContext(window.LangContext);
  const t = useT();
  const { loading, data, error, reload } = useAsync(async () => {
    // period enum ('MTD'); no i18n strings in API params.
    const [overview, trend] = await Promise.all([
      window.API.getOverview('MTD', '', '', 'openai'),
      window.API.getTrend('', '', '30d', 'openai'),
    ]);
    const latestPoint = trend?.length > 0 ? trend[trend.length - 1] : null;
    const fetchedAt = new Date().toLocaleString('zh-TW', { hour12: false }).replace(',', '');
    return { overview, latestPoint, trendCount: trend?.length || 0, fetchedAt };
  }, [lang]);

  const overview = data?.overview || {};
  const cards = [
    ['本月成本', `$${(overview.monthlyCost || 0).toFixed(2)}`],
    ['總請求數', (overview.totalRequests || 0).toLocaleString()],
    ['平均每日成本', `$${(overview.avgDailyCost || 0).toFixed(2)}`],
    ['趨勢資料天數', data?.trendCount || 0],
  ];

  return (
    <Card style={{ marginBottom: 12, padding: 16 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
        <div>
          <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-primary)' }}>OpenAI 即時狀態</div>
          <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 3 }}>
            查詢時間：{data?.fetchedAt || '—'}　最新趨勢日期：{data?.latestPoint?.date || '—'}
          </div>
          <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
            來源：/v1/usage/overview + /v1/usage/trend（source=openai）
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{
            fontSize: 11, fontWeight: 700, padding: '3px 8px', borderRadius: 999,
            background: error ? 'rgba(239,68,68,0.12)' : 'rgba(16,185,129,0.12)',
            color: error ? '#DC2626' : '#059669',
          }}>
            {error ? '異常' : '正常'}
          </span>
          <button onClick={reload} style={{
            padding: '6px 10px', borderRadius: 6, border: '1px solid var(--border)',
            background: 'var(--card-bg)', color: 'var(--text-secondary)', cursor: 'pointer', fontSize: 12
          }}>重新載入</button>
        </div>
      </div>

      {loading && <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>載入中…</div>}
      {error && <div style={{ fontSize: 12, color: '#DC2626' }}>OpenAI 快照載入失敗。</div>}

      {!loading && !error && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))', gap: 8 }}>
          {cards.map(([k, v]) => (
            <div key={k} style={{ border: '1px solid var(--border)', borderRadius: 8, padding: '10px 12px', background: 'var(--hover-bg)' }}>
              <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{k}</div>
              <div style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', marginTop: 2 }}>{v}</div>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}

function AzureSnapshot() {
  const { loading: ovLoading, data: ovData, error: ovError, reload: reloadOv } =
    useAsync(() => window.API.getAzureOverview(10), []);
  const { loading: stLoading, data: stData, error: stError, reload: reloadSt } =
    useAsync(() => window.API.getAzureStatus(), []);

  const loading = ovLoading || stLoading;
  const error = ovError || stError;
  function reload() { reloadOv(); reloadSt(); }

  const displaySyncAt = React.useMemo(() => {
    const raw = ovData?.lastSyncAtUtc;
    if (!raw) return '—';
    const dt = new Date(raw);
    if (Number.isNaN(dt.getTime())) return raw;
    return dt.toLocaleString('zh-TW', { hour12: false }).replace(',', '');
  }, [ovData?.lastSyncAtUtc]);

  const counts = stData?.counts || {};
  const cards = [
    ['成本紀錄', ovData?.totalRows ?? (counts.azure_cost_daily || 0)],
    ['AI 帳戶', counts.azure_ai_accounts || 0],
    ['部署', counts.azure_ai_deployments || 0],
    ['模型用量', counts.azure_openai_model_usage || 0],
  ];

  return (
    <Card style={{ marginBottom: 20, padding: 16 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 10 }}>
        <div>
          <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-primary)' }}>Azure 資料快照（DB 快取）</div>
          <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 3 }}>
            最後同步：{displaySyncAt}　來源：{ovData?.source || '—'}
          </div>
          <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
            來源：/v1/azure/overview + /v1/azure/status
          </div>
        </div>
        <button onClick={reload} style={{
          padding: '6px 10px', borderRadius: 6, border: '1px solid var(--border)',
          background: 'var(--card-bg)', color: 'var(--text-secondary)', cursor: 'pointer', fontSize: 12
        }}>重新載入</button>
      </div>

      {loading && <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>載入中…</div>}
      {error && <div style={{ fontSize: 12, color: '#DC2626' }}>Azure 快照載入失敗。</div>}

      {!loading && !error && (
        <>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))', gap: 8, marginBottom: 12 }}>
            {cards.map(([k, v]) => (
              <div key={k} style={{ border: '1px solid var(--border)', borderRadius: 8, padding: '10px 12px', background: 'var(--hover-bg)' }}>
                <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{k}</div>
                <div style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', marginTop: 2 }}>{Number(v).toLocaleString()}</div>
              </div>
            ))}
          </div>

          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 500 }}>
              <thead>
                <tr>
                  {['訂閱', '帳戶', '累計成本 (USD)'].map(h => (
                    <th key={h} style={{ textAlign: 'left', padding: '7px 8px', fontSize: 11, color: 'var(--text-muted)', borderBottom: '1px solid var(--border)' }}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {(ovData?.topAccounts || []).map((x, i) => (
                  <tr key={`${x.accountName}-${i}`}>
                    <td style={{ padding: '7px 8px', fontSize: 12, borderBottom: '1px solid var(--border)' }}>{x.subscriptionName || '—'}</td>
                    <td style={{ padding: '7px 8px', fontSize: 12, borderBottom: '1px solid var(--border)' }}>{x.accountName || '—'}</td>
                    <td style={{ padding: '7px 8px', fontSize: 12, fontWeight: 600, borderBottom: '1px solid var(--border)' }}>${(x.totalCostUsd || 0).toFixed(2)}</td>
                  </tr>
                ))}
                {!(ovData?.topAccounts?.length) && (
                  <tr><td colSpan={3} style={{ padding: 12, fontSize: 12, color: 'var(--text-muted)' }}>無資料</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Card>
  );
}

function DocsPage() {
  const t = useT();

  const frontendComponents = [
    ['圖表', ['LineChart', 'BarChart', 'DonutChart', 'SparkLine', 'HorizontalBar']],
    ['共用 UI', ['Card', 'Skeleton', 'EmptyState', 'ErrorState', 'StatCard', 'DeltaBadge']],
    ['版面', ['Sidebar', 'Header', 'TweaksPanel']],
    ['頁面', ['OverviewPage', 'UsagePage', 'CostReportPage', 'DeprecatedModelsPage', 'AzurePage', 'DocsPage']],
    ['Hooks', ['useAsync(fn, deps)', 'useT()', 'useTweaks(defaults)', 'useDebounce(val, ms)']],
    ['i18n', ['LangContext（React.createContext）', 'useT() → t(key)', 'TRANSLATIONS.en / .zh']],
  ];

  return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)', maxWidth: 960 }}>
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.3px' }}>
          {t('docs_title')}
        </h1>
        <p style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 6, lineHeight: 1.7, marginBottom: 0 }}>
          ASP.NET Core 8 後端 + 原生 JSX 前端（Babel in-browser，無建置步驟）。資料來源：OpenAI Admin API + Azure Cost Management API + Azure Monitor Metrics API，統一儲存至 MongoDB。
        </p>
      </div>

      <OpenAISnapshot />
      <AzureSnapshot />

      <DocSection title="專案結構">
        <CodeBlock code={CODE_BLOCKS.folderStructure} />
      </DocSection>

      <DocSection title="資料模型（C#）">
        <CodeBlock code={CODE_BLOCKS.domainModels} />
      </DocSection>

      <DocSection title="MongoDB 集合">
        <CodeBlock code={CODE_BLOCKS.mongoCollections} />
      </DocSection>

      <DocSection title="背景排程（DataFetchWorker）">
        <CodeBlock code={CODE_BLOCKS.workerSchedule} />
      </DocSection>

      <DocSection title="API 端點">
        <CodeBlock code={CODE_BLOCKS.apiEndpoints} />
      </DocSection>

      <DocSection title="前端元件">
        {frontendComponents.map(([group, items]) => (
          <div key={group} style={{ marginBottom: 12 }}>
            <div style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-muted)', marginBottom: 6,
              textTransform: 'uppercase', letterSpacing: '0.5px' }}>{group}</div>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
              {items.map(item => (
                <span key={item} style={{ padding: '3px 9px', background: 'var(--accent-subtle)',
                  color: 'var(--accent)', borderRadius: 5, fontSize: 12, fontWeight: 500 }}>{item}</span>
              ))}
            </div>
          </div>
        ))}
      </DocSection>
    </div>
  );
}

Object.assign(window, { DocsPage });
