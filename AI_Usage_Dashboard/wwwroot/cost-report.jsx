// cost-report.jsx — Cost Report page (i18n-aware, real API)

function fmt$(v) { return v >= 1000 ? `$${(v/1000).toFixed(2)}k` : `$${v.toFixed(2)}`; }
function resolveProjectLabel(projectIdOrLabel, projectNameById) {
  if (projectIdOrLabel && projectNameById[projectIdOrLabel]) return projectNameById[projectIdOrLabel];
  return projectIdOrLabel || '';
}

function SortIndicator({ active, dir }) {
  if (!active) return (
    <svg width="9" height="9" viewBox="0 0 9 9" fill="currentColor" style={{ opacity: 0.25, marginLeft: 3 }}>
      <polygon points="4.5,1 7,5 2,5"/><polygon points="4.5,8 7,4 2,4"/>
    </svg>
  );
  return dir === 'asc'
    ? <svg width="9" height="9" viewBox="0 0 9 9" fill="var(--accent)" style={{ marginLeft: 3 }}><polygon points="4.5,1 7,7 2,7"/></svg>
    : <svg width="9" height="9" viewBox="0 0 9 9" fill="var(--accent)" style={{ marginLeft: 3 }}><polygon points="4.5,8 7,2 2,2"/></svg>;
}

function CostTable({ title, rows, colA, loading, sortBy, sortDir, onSort }) {
  const t = useT();
  const maxCost = rows.length ? Math.max(...rows.map(r => r.cost)) : 1;

  function toggleSort(col) {
    const newDir = sortBy === col && sortDir === 'desc' ? 'asc' : 'desc';
    onSort(col, newDir);
  }

  if (loading) return (
    <Card>
      <Skeleton width="40%" height={14} />
      <div style={{ marginTop: 12 }}>
        {[1,2,3,4].map(i => <div key={i} style={{ marginBottom: 8 }}><Skeleton width="100%" height={11} /></div>)}
      </div>
    </Card>
  );

  if (!rows.length) return (
    <Card style={{ padding: 0 }}>
      <div style={{ padding: '14px 16px', borderBottom: '1px solid var(--border)', fontSize: 13,
        fontWeight: 600, color: 'var(--text-primary)' }}>{title}</div>
      <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />
    </Card>
  );

  const headers = [{ label: colA, col: 'label' }, { label: t('col_cost'), col: 'cost' }, { label: t('col_share'), col: null }];
  return (
    <Card style={{ padding: 0 }}>
      <div style={{ padding: '14px 16px', borderBottom: '1px solid var(--border)', fontSize: 13,
        fontWeight: 600, color: 'var(--text-primary)' }}>{title}</div>
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            {headers.map(h => (
              <th key={h.label} onClick={() => h.col && toggleSort(h.col)}
                style={{ padding: '8px 14px', fontSize: 11, fontWeight: 600, color: 'var(--text-muted)',
                  textAlign: h.label === colA ? 'left' : 'right', borderBottom: '1px solid var(--border)',
                  cursor: h.col ? 'pointer' : 'default', background: 'var(--table-header)',
                  userSelect: 'none' }}>
                {h.label}
                {h.col && <SortIndicator active={sortBy === h.col} dir={sortDir} />}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((r, idx) => (
            <tr key={r.key + idx}
              style={{ background: idx % 2 === 1 ? 'var(--table-stripe)' : 'transparent' }}
              onMouseEnter={e => e.currentTarget.style.background = 'var(--hover-bg)'}
              onMouseLeave={e => e.currentTarget.style.background = idx % 2 === 1 ? 'var(--table-stripe)' : 'transparent'}>
              <td style={{ padding: '9px 14px', fontSize: 12, color: 'var(--text-secondary)',
                borderBottom: '1px solid var(--border)', maxWidth: 180, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                {r.key}
              </td>
              <td style={{ padding: '9px 14px', fontSize: 12, fontWeight: 600, color: 'var(--accent)',
                textAlign: 'right', borderBottom: '1px solid var(--border)', fontVariantNumeric: 'tabular-nums' }}>
                {fmt$(r.cost)}
              </td>
              <td style={{ padding: '9px 14px', textAlign: 'right', borderBottom: '1px solid var(--border)', width: 110 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6, justifyContent: 'flex-end' }}>
                  <div style={{ flex: 1, height: 5, background: 'var(--border)', borderRadius: 3, maxWidth: 60, overflow: 'hidden' }}>
                    <div style={{ width: `${(r.cost / maxCost) * 100}%`, height: '100%',
                      background: 'var(--accent)', borderRadius: 3, opacity: 0.7 }} />
                  </div>
                  <span style={{ fontSize: 11, color: 'var(--text-muted)', minWidth: 34, textAlign: 'right' }}>
                    {(r.pct || 0).toFixed(1)}%
                  </span>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </Card>
  );
}

function CostReportPage({ dateRange, orgId, projectId, projectMap, source }) {
  const t = useT();
  const [projSort,  setProjSort]  = React.useState({ col: 'cost', dir: 'desc' });
  const [modelSort, setModelSort] = React.useState({ col: 'cost', dir: 'desc' });
  const [dateSort,  setDateSort]  = React.useState({ col: 'cost', dir: 'desc' });

  const { loading: projLoading, data: projRaw } =
    useAsync(() => window.API.getCostBreakdown('project', dateRange, orgId, projectId, source, projSort.col, projSort.dir),
             [dateRange, orgId, projectId, source, projSort]);
  const { loading: modelLoading, data: modelRaw } =
    useAsync(() => window.API.getCostBreakdown('model', dateRange, orgId, projectId, source, modelSort.col, modelSort.dir),
             [dateRange, orgId, projectId, source, modelSort]);
  const { loading: dateLoading, data: dateRaw } =
    useAsync(() => window.API.getCostBreakdown('date', dateRange, orgId, projectId, source, dateSort.col, dateSort.dir),
             [dateRange, orgId, projectId, source, dateSort]);

  const projectNameById = React.useMemo(
    () => Object.fromEntries((projectMap || []).map(p => [p.projectId, p.projectName])),
    [projectMap]
  );
  const byProject = (projRaw?.items  || []).map(x => ({ key: resolveProjectLabel(x.key, projectNameById), cost: x.cost, pct: x.pct }));
  const byModel   = (modelRaw?.items || []).map(x => ({ key: x.key, cost: x.cost, pct: x.pct }));
  const byDate    = (dateRaw?.items  || []).slice(0, 10).map(x => ({ key: x.key, cost: x.cost, pct: x.pct }));

  // totalCost comes from backend (resp.totalCostUsd) — never recomputed client-side.
  const totalCost = projRaw?.totalCostUsd || 0;

  return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)', maxWidth: 1300 }}>
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.3px' }}>
          {t('cost_title')}
        </h1>
        <p style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4, marginBottom: 0 }}>
          {t('cost_period')} <strong style={{ color: 'var(--text-primary)' }}>{fmt$(totalCost)}</strong>
        </p>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))', gap: 14, marginBottom: 14 }}>
        <CostTable title={t('by_project')} rows={byProject} colA={t('col_project')} loading={projLoading}
          sortBy={projSort.col} sortDir={projSort.dir}
          onSort={(col, dir) => setProjSort({ col, dir })} />
        <CostTable title={t('by_model')}   rows={byModel}   colA={t('col_model')}   loading={modelLoading}
          sortBy={modelSort.col} sortDir={modelSort.dir}
          onSort={(col, dir) => setModelSort({ col, dir })} />
      </div>
      <CostTable title={t('top_days')} rows={byDate} colA={t('col_date')} loading={dateLoading}
        sortBy={dateSort.col} sortDir={dateSort.dir}
        onSort={(col, dir) => setDateSort({ col, dir })} />
    </div>
  );
}

Object.assign(window, { CostReportPage });
