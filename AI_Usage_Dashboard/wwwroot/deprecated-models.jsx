// deprecated-models.jsx — Deprecated Models page (i18n-aware, real API)

function fmtN(v) { return v >= 1e6 ? `${(v/1e6).toFixed(1)}M` : v >= 1e3 ? `${(v/1e3).toFixed(1)}K` : String(v); }
function fmt$(v) { return v >= 1000 ? `$${(v/1000).toFixed(2)}k` : `$${v.toFixed(2)}`; }
function resolveProjectLabel(projectIdOrLabel, projectNameById) {
  if (projectIdOrLabel && projectNameById[projectIdOrLabel]) return projectNameById[projectIdOrLabel];
  return projectIdOrLabel || '';
}
function formatProjectGroup(projects, projectNameById) {
  const list = (projects || []).map(p => resolveProjectLabel(p, projectNameById)).filter(Boolean);
  return list.length ? list.join(', ') : '—';
}

const URGENCY_CFG = {
  expired:  { bg: 'rgba(239,68,68,0.1)',   text: '#DC2626', border: 'rgba(239,68,68,0.25)' },
  critical: { bg: 'rgba(249,115,22,0.1)',  text: '#EA580C', border: 'rgba(249,115,22,0.25)' },
  warning:  { bg: 'rgba(245,158,11,0.12)', text: '#D97706', border: 'rgba(245,158,11,0.3)' },
  upcoming: { bg: 'rgba(99,102,241,0.08)', text: '#6366F1', border: 'rgba(99,102,241,0.2)' },
};

function UrgencyBadge({ urgency }) {
  const t = useT();
  const cfg = URGENCY_CFG[urgency] || URGENCY_CFG.upcoming;
  const key = `urg_${urgency}`;
  return (
    <span style={{ display: 'inline-block', padding: '3px 8px', borderRadius: 5,
      fontSize: 11, fontWeight: 700, background: cfg.bg, color: cfg.text,
      border: `1px solid ${cfg.border}` }}>
      {t(key)}
    </span>
  );
}

function DepKpiCard({ label, value, urgency, loading }) {
  if (loading) return (
    <Card style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <Skeleton width="60%" height={11} />
      <Skeleton width="40%" height={28} />
    </Card>
  );
  const cfg = urgency ? URGENCY_CFG[urgency] : null;
  return (
    <Card style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <div style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 500,
        textTransform: 'uppercase', letterSpacing: '0.3px' }}>{label}</div>
      <div style={{ fontSize: 28, fontWeight: 700, letterSpacing: '-0.5px', lineHeight: 1,
        color: cfg ? cfg.text : 'var(--text-primary)' }}>{value}</div>
    </Card>
  );
}

function DeprecatedModelsPage({ orgId, projectId, projectMap, source, dateRange }) {
  const showOpenAi = source !== 'azure';
  const showAzure  = source !== 'openai';
  const t = useT();
  const [sortCol, setSortCol] = React.useState('daysUntilShutdown');
  const [sortDir, setSortDir] = React.useState('asc');
  const projectNameById = React.useMemo(
    () => Object.fromEntries((projectMap || []).map(p => [p.projectId, p.projectName])),
    [projectMap]
  );

  // Server-side sort: state changes re-fetch with new sortBy/sortDir.
  const { loading, data, error, reload } =
    useAsync(() => window.API.getDeprecatedModels(orgId, projectId, source, dateRange, sortCol, sortDir),
             [orgId, projectId, source, dateRange, sortCol, sortDir]);

  const summary = data || { totalDeprecated: 0, expired: 0, critical: 0, warning: 0, upcoming: 0, models: [] };

  function toggleSort(col) {
    setSortDir(d => sortCol === col ? (d === 'asc' ? 'desc' : 'asc') : 'asc');
    setSortCol(col);
  }

  const sorted = summary.models || [];

  function SortIcon({ col }) {
    if (sortCol !== col) return (
      <svg width="10" height="10" viewBox="0 0 10 10" fill="currentColor" style={{ opacity: 0.25 }}>
        <polygon points="5,1 8,5 2,5"/><polygon points="5,9 8,5 2,5"/>
      </svg>
    );
    return sortDir === 'asc'
      ? <svg width="10" height="10" viewBox="0 0 10 10" fill="var(--accent)"><polygon points="5,1 8,7 2,7"/></svg>
      : <svg width="10" height="10" viewBox="0 0 10 10" fill="var(--accent)"><polygon points="5,9 8,3 2,3"/></svg>;
  }

  const thStyle = { padding: '8px 14px', fontSize: 11, fontWeight: 600, color: 'var(--text-muted)',
    cursor: 'pointer', userSelect: 'none', borderBottom: '1px solid var(--border)',
    background: 'var(--table-header)', whiteSpace: 'nowrap' };

  const cols = [
    { key: 'modelName',          label: t('col_model'),    align: 'left'  },
    { key: 'substituteModel',    label: t('dep_substitute'), align: 'left'  },
    { key: 'shutdownDate',       label: t('dep_shutdown'), align: 'left'  },
    { key: 'daysUntilShutdown',  label: t('dep_days'),     align: 'right' },
    { key: 'urgency',            label: t('dep_urgency'),  align: 'left'  },
    { key: 'totalRequests',      label: t('col_requests'), align: 'right' },
    { key: 'lastSeenDate',       label: t('dep_last_seen'),align: 'left'  },
    { key: 'projects',           label: t('dep_projects'), align: 'left'  },
  ];

  if (error) return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)' }}>
      <ErrorState onRetry={reload} />
    </div>
  );

  return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)', maxWidth: 1300 }}>
      <div style={{ marginBottom: 20, display: 'flex', alignItems: 'center', gap: 12 }}>
        <h1 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.3px' }}>
          {t('dep_title')}
        </h1>
        <div style={{ flex: 1 }} />
        <a href="https://developers.openai.com/api/docs/deprecations"
           target="_blank" rel="noopener noreferrer"
           title={t('dep_official_link_tip')}
           style={{
             display: 'inline-flex', alignItems: 'center', gap: 4,
             padding: '4px 10px', borderRadius: 6,
             background: 'var(--accent-subtle)', color: 'var(--accent)',
             textDecoration: 'none', fontSize: 11, fontWeight: 600,
             border: '1px solid var(--accent)',
           }}>
          {t('dep_official_link')}
          <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4">
            <path d="M14 3h7v7"/><path d="M21 3l-9 9"/><path d="M21 14v5a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h5"/>
          </svg>
        </a>
      </div>

      {/* KPI Row */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(170px, 1fr))', gap: 14, marginBottom: 20 }}>
        <DepKpiCard loading={loading} label={t('dep_total')}    value={summary.totalDeprecated} />
        <DepKpiCard loading={loading} label={t('dep_expired')}  value={summary.expired}  urgency="expired" />
        <DepKpiCard loading={loading} label={t('dep_critical')} value={summary.critical} urgency="critical" />
        <DepKpiCard loading={loading} label={t('dep_warning')}  value={summary.warning}  urgency="warning" />
        <DepKpiCard loading={loading} label={t('dep_upcoming')} value={summary.upcoming} urgency="upcoming" />
      </div>

      <Card style={{ padding: 0, overflow: 'hidden' }}>
        {loading ? (
          <div style={{ padding: 24 }}>
            {[1,2,3,4,5].map(i => (
              <div key={i} style={{ display: 'flex', gap: 14, marginBottom: 12 }}>
                {[200,100,80,90,80,80,100].map((w,j) => <Skeleton key={j} width={w} height={12} />)}
              </div>
            ))}
          </div>
        ) : sorted.length === 0 ? (
          <div style={{ padding: '60px 0' }}>
            <EmptyState
              title={t('dep_no_issues')}
              description="All models in active use are current."
              icon="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z"
            />
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 900 }}>
              <thead>
                <tr>
                  {cols.map(col => (
                    <th key={col.key} onClick={() => toggleSort(col.key)}
                      style={{ ...thStyle, textAlign: col.align }}>
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                        {col.label} <SortIcon col={col.key} />
                      </span>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {sorted.map((m, i) => {
                  const cfg = URGENCY_CFG[m.urgency] || URGENCY_CFG.upcoming;
                  const rowBg = i % 2 === 0 ? 'transparent' : 'var(--table-stripe)';
                  const td = { padding: '10px 14px', fontSize: 12, color: 'var(--text-secondary)',
                    borderBottom: '1px solid var(--border)', whiteSpace: 'nowrap' };
                  return (
                    <tr key={m.modelName}
                      style={{ background: rowBg }}
                      onMouseEnter={e => e.currentTarget.style.background = 'var(--hover-bg)'}
                      onMouseLeave={e => e.currentTarget.style.background = rowBg}>
                      <td style={{ ...td, fontWeight: 600, color: 'var(--text-primary)', maxWidth: 200,
                        overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        <span title={m.modelName}>{m.modelName}</span>
                      </td>
                      <td style={{ ...td, maxWidth: 180, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        <span title={m.substituteModel || ''} style={{ color: 'var(--accent)', fontWeight: 600 }}>
                          {m.substituteModel || '—'}
                        </span>
                      </td>
                      <td style={{ ...td, fontFamily: 'monospace', fontSize: 11 }}>
                        {m.shutdownDate || '—'}
                      </td>
                      <td style={{ ...td, textAlign: 'right', fontVariantNumeric: 'tabular-nums',
                        fontWeight: 700, color: cfg.text }}>
                        {m.daysUntilShutdown <= 0 ? '—' : m.daysUntilShutdown}
                      </td>
                      <td style={td}>
                        <UrgencyBadge urgency={m.urgency} />
                      </td>
                      <td style={{ ...td, textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>
                        {fmtN(m.totalRequests)}
                      </td>
                      <td style={{ ...td, fontFamily: 'monospace', fontSize: 11 }}>
                        {m.lastSeenDate || '—'}
                      </td>
                      <td style={{ ...td, maxWidth: 340, whiteSpace: 'normal' }}>
                        {(() => {
                          const openAiProjects = formatProjectGroup(m.openAiProjects, projectNameById);
                          const azureProjects = formatProjectGroup(m.azureProjects, projectNameById);
                          const titleParts = [];
                          if (showOpenAi) titleParts.push(`(openai) ${openAiProjects}`);
                          if (showAzure)  titleParts.push(`(azure) ${azureProjects}`);
                          return (
                            <div title={titleParts.join('\n')} style={{ display: 'grid', gap: 2, fontSize: 11, lineHeight: 1.35 }}>
                              {showOpenAi && (
                                <div style={{ overflow: 'hidden', textOverflow: 'ellipsis', color: 'var(--text-primary)' }}>
                                  {showAzure && (
                                    <><span style={{ color: 'var(--text-muted)', fontWeight: 600 }}>(openai)</span>{' '}</>
                                  )}
                                  <span style={{ fontWeight: 700 }}>{openAiProjects}</span>
                                </div>
                              )}
                              {showAzure && (
                                <div style={{ overflow: 'hidden', textOverflow: 'ellipsis', color: 'var(--text-primary)' }}>
                                  {showOpenAi && (
                                    <><span style={{ color: 'var(--text-muted)', fontWeight: 600 }}>(azure)</span>{' '}</>
                                  )}
                                  <span style={{ fontWeight: 700 }}>{azureProjects}</span>
                                </div>
                              )}
                            </div>
                          );
                        })()}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  );
}

Object.assign(window, { DeprecatedModelsPage });
