// api.js — API service layer for AI Usage Dashboard
// All requests go to the same origin (backend serves frontend from wwwroot).
//
// Frontend rule: components hold a period ENUM ('today' | '7d' | '30d' | 'MTD' |
// '__custom__|YYYY-MM-DD|YYYY-MM-DD'). No i18n labels in state, no reverse-lookup
// (architecture principle ③).

const API_BASE = '';

// ── useAsync hook ─────────────────────────────────────────────────────────────
function useAsync(asyncFn, deps) {
  const [state, setState] = React.useState({ loading: true, data: null, error: null });
  const fnRef = React.useRef(asyncFn);
  fnRef.current = asyncFn;
  const reload = React.useCallback(() => {
    let cancelled = false;
    setState(s => ({ ...s, loading: true, error: null }));
    fnRef.current()
      .then(data => { if (!cancelled) setState({ loading: false, data, error: null }); })
      .catch(err => { if (!cancelled) setState({ loading: false, data: null, error: err }); });
    return () => { cancelled = true; };
  }, []);
  React.useEffect(() => reload(), deps);
  return { ...state, reload };
}

// ── Date helpers ──────────────────────────────────────────────────────────────
// `period` is always a backend enum: 'today' | '7d' | '30d' | 'MTD'.
// Custom range encodes start/end inside the value: `__custom__|YYYY-MM-DD|YYYY-MM-DD`.
function dateRangeQuery(period) {
  if (typeof period === 'string' && period.startsWith('__custom__|')) {
    const parts = period.split('|');
    const startDate = parts[1] || '';
    const endDate   = parts[2] || '';
    if (startDate && endDate) return { period: 'custom', startDate, endDate };
  }
  return { period: period || 'MTD' };
}

function fmtDate(isoStr) {
  if (!isoStr) return '';
  return String(isoStr).slice(0, 10);
}

// ── Base fetch ────────────────────────────────────────────────────────────────
async function apiFetch(path) {
  const res = await fetch(API_BASE + path);
  if (!res.ok) {
    const body = await res.text().catch(() => '');
    throw new Error(`HTTP ${res.status}: ${body.slice(0, 120)}`);
  }
  return res.json();
}

async function apiPost(path, body) {
  const res = await fetch(API_BASE + path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

async function apiPut(path, body) {
  const res = await fetch(API_BASE + path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

// ── API methods ───────────────────────────────────────────────────────────────
window.API = {

  async getOverview(period, orgId, projectId, source = 'all') {
    const q = new URLSearchParams({ source, ...dateRangeQuery(period) });
    if (orgId)     q.set('orgId',     orgId);
    if (projectId) q.set('projectId', projectId);
    return apiFetch(`/v1/usage/overview?${q}`);
  },

  async getTrend(orgId, projectId, period = '30d', source = 'all') {
    const q = new URLSearchParams({ source, ...dateRangeQuery(period) });
    if (orgId)     q.set('orgId',     orgId);
    if (projectId) q.set('projectId', projectId);
    return apiFetch(`/v1/usage/trend?${q}`);
  },

  async getCostBreakdown(groupBy, period, orgId, projectId, source = 'all', sortBy = 'cost', sortDir = 'desc') {
    const q = new URLSearchParams({ groupBy, source, sortBy, sortDir, ...dateRangeQuery(period) });
    if (orgId)     q.set('orgId',     orgId);
    if (projectId) q.set('projectId', projectId);
    const resp = await apiFetch(`/v1/cost/breakdown?${q}`);
    return {
      items: (resp.items || []).map(x => ({
        key:      x.label,
        cost:     x.costUsd,
        pct:      x.percentage,
        requests: x.requests,
      })),
      totalCostUsd:      resp.totalCostUsd      || 0,
      totalRequests:     resp.totalRequests     || 0,
      totalInputTokens:  resp.totalInputTokens  || 0,
      totalOutputTokens: resp.totalOutputTokens || 0,
    };
  },

  async getCostTrendStacked(period, orgId, projectId, source = 'all', topN = 10) {
    const q = new URLSearchParams({ source, topN, ...dateRangeQuery(period) });
    if (orgId)     q.set('orgId',     orgId);
    if (projectId) q.set('projectId', projectId);
    return apiFetch(`/v1/cost/trend-stacked?${q}`);
  },

  async getUsageRecords({ source = 'all', orgId, projectId, model, capability, groupBy, search, sortBy, sortDir, page, pageSize, period } = {}) {
    const q = new URLSearchParams({ source, page: page || 1, pageSize: pageSize || 15, ...dateRangeQuery(period || 'MTD') });
    if (orgId)                          q.set('orgId',     orgId);
    if (projectId)                      q.set('projectId', projectId);
    if (model && model !== 'All')       q.set('model',     model);
    if (capability && capability !== 'All') q.set('capability', capability);
    if (groupBy)                        q.set('groupBy',   groupBy);
    if (search)                         q.set('search',    search);
    if (sortBy)                         q.set('sortBy',    sortBy);
    if (sortDir)                        q.set('sortDir',   sortDir || 'desc');
    const resp = await apiFetch(`/v1/usage/records?${q}`);
    return {
      data: (resp.data || []).map(r => ({
        date:         fmtDate(r.date),
        project:      r.projectName || '',
        projectId:    r.projectId,
        user:         r.userName,
        apiKey:       r.apiKeyName,
        model:        r.model,
        capability:   r.capability,
        inputTokens:  r.inputTokens,
        outputTokens: r.outputTokens,
        requests:     r.requests,
        cost:         r.costUsd,
        serviceTier:  r.serviceTier,
      })),
      total:      resp.total,
      page:       resp.page,
      pageSize:   resp.pageSize,
      totalPages: resp.totalPages,
    };
  },

  async getBudgets() {
    const resp = await apiFetch('/v1/budgets');
    return {
      items: (resp.items || []).map(b => ({
        projectId:     b.projectId,
        project:       b.projectName,
        monthlyBudget: b.monthlyBudget,
        spent:         b.spent,
        pct:           b.pct,
        remaining:     b.remaining,
        level:         b.level,
      })),
      summary: resp.summary || { critical: 0, warning: 0, ok: 0 },
    };
  },

  async putBudget(projectId, monthlyBudget) {
    const b = await apiPut(`/v1/budgets/${encodeURIComponent(projectId)}`, { monthlyBudget });
    return {
      projectId:     b.projectId,
      project:       b.projectName,
      monthlyBudget: b.monthlyBudget,
      spent:         b.spent,
      pct:           b.pct,
      remaining:     b.remaining,
      level:         b.level,
    };
  },

  async getAlerts(limit = 50, period = 'MTD') {
    const q = new URLSearchParams({ limit, ...dateRangeQuery(period) });
    const items = await apiFetch(`/v1/alerts?${q}`);
    return items.map((a, i) => ({
      id:      i,
      ts:      a.timestamp ? new Date(a.timestamp).toLocaleString('zh-TW', { hour12: false }).replace(',', '') : '',
      project: a.projectId,
      level:   a.level,
      message: a.message,
    }));
  },

  async createExport(type, filters) {
    const typeVal = type === 'cost' ? 'cost' : 'usage';
    return apiPost('/v1/export', { type: typeVal, filters });
  },

  async getExport(jobId) {
    return apiFetch(`/v1/export/${encodeURIComponent(jobId)}`);
  },

  async getProjects(orgId, source = 'openai') {
    const q = new URLSearchParams();
    if (orgId)  q.set('orgId',  orgId);
    if (source) q.set('source', source);
    return apiFetch(`/v1/projects?${q}`);
  },

  async getOrgs(source = 'openai') {
    const q = new URLSearchParams();
    if (source) q.set('source', source);
    return apiFetch(`/v1/orgs?${q}`);
  },

  async getUsageFilters(source = 'all') {
    const q = new URLSearchParams({ source });
    return apiFetch(`/v1/usage/filters?${q}`);
  },

  async getDeprecatedModels(orgId, projectId, source = 'all', period = 'MTD', sortBy = 'daysUntilShutdown', sortDir = 'asc') {
    const q = new URLSearchParams({ source, sortBy, sortDir, ...dateRangeQuery(period) });
    if (orgId)     q.set('orgId',     orgId);
    if (projectId) q.set('projectId', projectId);
    return apiFetch(`/v1/models/deprecated?${q}`);
  },

  async getAzureOverview(limit = 20, subscriptionId = '') {
    const q = new URLSearchParams({ limit });
    if (subscriptionId) q.set('subscriptionId', subscriptionId);
    return apiFetch(`/v1/azure/overview?${q}`);
  },

  async getAzureStatus(subscriptionId = '') {
    const q = new URLSearchParams();
    if (subscriptionId) q.set('subscriptionId', subscriptionId);
    const qs = q.toString();
    return apiFetch(qs ? `/v1/azure/status?${qs}` : '/v1/azure/status');
  },

  async getAzureQuery(dataset, { page = 1, pageSize = 20, search = '',
                                  subscriptionId = '', accountName = '', period = '' } = {}) {
    const q = new URLSearchParams({ dataset, page, pageSize });
    if (search)         q.set('search', search);
    if (subscriptionId) q.set('subscriptionId', subscriptionId);
    if (accountName)    q.set('accountName',    accountName);
    if (period) {
      // Custom range encodes start/end inside the value (same convention as dateRangeQuery).
      const dr = dateRangeQuery(period);
      Object.entries(dr).forEach(([k, v]) => q.set(k, v));
    }
    return apiFetch(`/v1/azure/query?${q}`);
  },
};

window.useAsync         = useAsync;
window.dateRangeQuery   = dateRangeQuery;
