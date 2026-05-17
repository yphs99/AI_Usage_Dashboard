// usage-detail.jsx — Usage Detail page (i18n-aware, server-side pagination)

const CAPABILITY_COLORS = {
  'Chat Completions': 'blue', 'Responses': 'blue',
  'Embeddings': 'green', 'Images': 'amber',
  'Text to Speech': 'gray', 'Transcription': 'gray',
  'Moderation': 'red', 'Vector Stores': 'purple', 'Code Interpreter': 'purple',
};
const MODEL_TIER = {
  'gpt-4o': 'red', 'gpt-4o-mini': 'blue', 'gpt-4-turbo': 'red',
  'gpt-3.5-turbo': 'green', 'text-embedding-3-large': 'gray',
  'text-embedding-3-small': 'gray', 'dall-e-3': 'amber',
  'whisper-1': 'gray', 'tts-1': 'gray',
};


function fmtN(v) { return v >= 1e6 ? `${(v/1e6).toFixed(1)}M` : v >= 1e3 ? `${(v/1e3).toFixed(1)}K` : String(v); }
function fmt$(v) { return v < 0.01 ? `$${v.toFixed(4)}` : `$${v.toFixed(2)}`; }

// groupBy options: state stores the API enum value directly; label is i18n only.

function FilterBar({ filters, setFilters, projectMap, source }) {
  const t = useT();
  const { data: filtersData } = useAsync(() => window.API.getUsageFilters(source), [source]);
  const models = ['All', ...(filtersData?.models       || [])];
  const caps   = ['All', ...(filtersData?.capabilities || [])];
  const projs   = [{ value: '', label: t('all_projects') }, ...(projectMap || []).map(p => ({
    value: p.projectId,
    label: p.projectName || '',
  }))];
  const groups  = [
    { value: '',            label: t('group_date')    },
    { value: 'project',     label: t('group_project') },
    { value: 'user',        label: t('group_user')    },
    { value: 'apikey',      label: t('group_apikey')  },
    { value: 'model',       label: t('group_model')   },
    { value: 'servicetier', label: t('group_tier')    },
  ];

  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 14 }}>
      {source !== 'all' && (
        <Select label={t('label_project')} value={filters.projectId || ''} options={projs}
          onChange={v => setFilters(f => ({...f, projectId: v || ''}))} minWidth={140} />
      )}
      <Select label={t('filter_model')} value={filters.model} options={models}
        onChange={v => setFilters(f => ({...f, model: v}))} minWidth={160} />
      <Select label={t('filter_cap')} value={filters.capability} options={caps}
        onChange={v => setFilters(f => ({...f, capability: v}))} minWidth={155} />
      <div style={{ width: 1, height: 22, background: 'var(--border)', margin: '0 2px' }} />
      <Select label={t('filter_group')} value={filters.groupBy} options={groups}
        onChange={v => setFilters(f => ({...f, groupBy: v}))} minWidth={130} />
      <div style={{ flex: 1 }} />
      <div style={{ position: 'relative' }}>
        <input placeholder={t('search_ph')} value={filters.search}
          onChange={e => setFilters(f => ({...f, search: e.target.value}))}
          style={{ padding: '6px 10px 6px 30px', background: 'var(--card-bg)', border: '1px solid var(--border)',
            borderRadius: 7, fontSize: 12, color: 'var(--text-primary)', outline: 'none', width: 180 }}
        />
        <svg style={{ position: 'absolute', left: 8, top: '50%', transform: 'translateY(-50%)',
          color: 'var(--text-muted)', pointerEvents: 'none' }}
          width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
        </svg>
      </div>
    </div>
  );
}

function SortIcon({ col, sort }) {
  if (sort.col !== col) return <svg width="10" height="10" viewBox="0 0 10 10" fill="currentColor" style={{ opacity: 0.25 }}><polygon points="5,1 8,5 2,5"/><polygon points="5,9 8,5 2,5"/></svg>;
  return sort.dir === 'asc'
    ? <svg width="10" height="10" viewBox="0 0 10 10" fill="var(--accent)"><polygon points="5,1 8,7 2,7"/></svg>
    : <svg width="10" height="10" viewBox="0 0 10 10" fill="var(--accent)"><polygon points="5,9 8,3 2,3"/></svg>;
}

function UsageTable({ rows, loading, sort, setSort, page, setPage, pageSize, total }) {
  const t = useT();
  const cols = [
    { key: 'date',         label: t('col_date'),       w: 88 },
    { key: 'project',      label: t('col_project'),    w: 140 },
    { key: 'user',         label: t('col_user'),       w: 150 },
    { key: 'model',        label: t('col_model'),      w: 140 },
    { key: 'capability',   label: t('col_cap'),        w: 130 },
    { key: 'inputTokens',  label: t('col_input_tok'),  w: 100, align: 'right' },
    { key: 'outputTokens', label: t('col_output_tok'), w: 100, align: 'right' },
    { key: 'requests',     label: t('col_requests'),   w: 80,  align: 'right' },
    { key: 'cost',         label: t('col_cost'),       w: 80,  align: 'right' },
  ];

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  function toggleSort(col) {
    setSort(s => s.col === col ? { col, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { col, dir: 'desc' });
    setPage(1);
  }

  const thStyle = col => ({
    padding: '8px 12px', fontSize: 11, fontWeight: 600, color: 'var(--text-muted)',
    textAlign: col.align || 'left', cursor: 'pointer', userSelect: 'none',
    whiteSpace: 'nowrap', borderBottom: '1px solid var(--border)',
    background: 'var(--table-header)',
  });
  const tdStyle = (col, i) => ({
    padding: '9px 12px', fontSize: 12, color: 'var(--text-secondary)',
    textAlign: col.align || 'left', borderBottom: '1px solid var(--border)',
    whiteSpace: 'nowrap', maxWidth: col.w, overflow: 'hidden', textOverflow: 'ellipsis',
  });

  if (loading) return (
    <div style={{ padding: 24 }}>
      {[1,2,3,4,5].map(i => (
        <div key={i} style={{ display: 'flex', gap: 12, marginBottom: 12 }}>
          {cols.map(c => <Skeleton key={c.key} width={c.w} height={12} />)}
        </div>
      ))}
    </div>
  );

  if (!rows.length) return <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />;

  return (
    <div>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 900 }}>
          <thead>
            <tr>
              {cols.map(col => (
                <th key={col.key} style={thStyle(col)} onClick={() => toggleSort(col.key)}>
                  <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                    {col.label} <SortIcon col={col.key} sort={sort} />
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, i) => (
              <tr key={i}
                style={{ background: i % 2 === 1 ? 'var(--table-stripe)' : 'transparent' }}
                onMouseEnter={e => e.currentTarget.style.background = 'var(--hover-bg)'}
                onMouseLeave={e => e.currentTarget.style.background = i % 2 === 1 ? 'var(--table-stripe)' : 'transparent'}>
                <td style={tdStyle(cols[0], i)}><span style={{ fontFamily: 'monospace', fontSize: 11 }}>{row.date}</span></td>
                <td style={tdStyle(cols[1], i)}><span title={row.project}>{row.project}</span></td>
                <td style={tdStyle(cols[2], i)}><span style={{ fontSize: 11, color: 'var(--text-muted)' }} title={row.user}>{row.user}</span></td>
                <td style={tdStyle(cols[3], i)}><Badge label={row.model} color={MODEL_TIER[row.model] || 'gray'} /></td>
                <td style={tdStyle(cols[4], i)}>
                  {row.capability
                    ? <Badge label={row.capability} color={CAPABILITY_COLORS[row.capability] || 'gray'} />
                    : <span style={{ color: 'var(--text-muted)', fontSize: 11 }}>—</span>}
                </td>
                <td style={{...tdStyle(cols[5], i), fontVariantNumeric: 'tabular-nums', color: 'var(--text-primary)'}}>{fmtN(row.inputTokens)}</td>
                <td style={{...tdStyle(cols[6], i), fontVariantNumeric: 'tabular-nums', color: 'var(--text-primary)'}}>{fmtN(row.outputTokens)}</td>
                <td style={{...tdStyle(cols[7], i), fontVariantNumeric: 'tabular-nums'}}>{fmtN(row.requests)}</td>
                <td style={{...tdStyle(cols[8], i), fontVariantNumeric: 'tabular-nums', fontWeight: 600, color: 'var(--accent)'}}>{fmt$(row.cost)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        padding: '12px 16px', borderTop: '1px solid var(--border)' }}>
        <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>
          {t('showing')} <strong style={{ color: 'var(--text-primary)' }}>{(page-1)*pageSize+1}–{Math.min(page*pageSize, total)}</strong>
          {' '}{t('of')}{' '}
          <strong style={{ color: 'var(--text-primary)' }}>{total}</strong> {t('records')}
        </div>
        <div style={{ display: 'flex', gap: 4 }}>
          {[...new Set([1, Math.max(1,page-1), page, Math.min(totalPages,page+1), totalPages])]
            .filter(p => p >= 1 && p <= totalPages)
            .sort((a,b) => a-b)
            .map((p, i, arr) => {
              const gap = i > 0 && p - arr[i-1] > 1;
              return (
                <React.Fragment key={p}>
                  {gap && <span style={{ padding: '4px 4px', fontSize: 12, color: 'var(--text-muted)' }}>…</span>}
                  <button onClick={() => setPage(p)} style={{
                    padding: '4px 9px', borderRadius: 5, border: '1px solid var(--border)',
                    background: p === page ? 'var(--accent)' : 'var(--card-bg)',
                    color: p === page ? 'white' : 'var(--text-secondary)',
                    fontSize: 12, cursor: 'pointer', fontWeight: p === page ? 600 : 400,
                  }}>{p}</button>
                </React.Fragment>
              );
            })
          }
        </div>
        <div style={{ display: 'flex', gap: 6 }}>
          <button onClick={() => setPage(p => Math.max(1, p-1))} disabled={page === 1} style={{
            padding: '5px 10px', borderRadius: 6, border: '1px solid var(--border)',
            background: 'var(--card-bg)', color: 'var(--text-secondary)', fontSize: 12, cursor: 'pointer',
            opacity: page === 1 ? 0.4 : 1,
          }}>{t('prev')}</button>
          <button onClick={() => setPage(p => Math.min(totalPages, p+1))} disabled={page === totalPages} style={{
            padding: '5px 10px', borderRadius: 6, border: '1px solid var(--border)',
            background: 'var(--card-bg)', color: 'var(--text-secondary)', fontSize: 12, cursor: 'pointer',
            opacity: page === totalPages ? 0.4 : 1,
          }}>{t('next')}</button>
        </div>
      </div>
    </div>
  );
}

function UsagePage({ dateRange, orgId, projectId, projectMap, source }) {
  const t = useT();
  const pageSize = 15;

  const [filters, setFilters] = React.useState({
    projectId: projectId || '', model: 'All', capability: 'All',
    groupBy: '', search: '',
  });
  const [sort, setSort] = React.useState({ col: 'date', dir: 'desc' });
  const [page, setPage] = React.useState(1);

  // Reset to page 1 on filter/sort change
  React.useEffect(() => { setPage(1); }, [filters, sort]);

  const filterProjectId = React.useMemo(() => {
    if (source === 'all') return '';
    return filters.projectId || '';
  }, [source, filters.projectId]);

  React.useEffect(() => {
    setFilters(f => (f.projectId === (projectId || '') ? f : { ...f, projectId: projectId || '' }));
  }, [projectId]);

  React.useEffect(() => {
    if (source === 'all') {
      setFilters(f => (f.projectId === '' ? f : { ...f, projectId: '' }));
    }
  }, [source]);

  const { loading, data: result, error, reload } = useAsync(
    () => window.API.getUsageRecords({
      source,
      orgId,
      projectId:  filterProjectId,
      model:      filters.model !== 'All' ? filters.model : '',
      capability: filters.capability !== 'All' ? filters.capability : '',
      groupBy:    filters.groupBy,
      search:     filters.search,
      sortBy:     sort.col,
      sortDir:    sort.dir,
      page,
      pageSize,
      dateRange,
    }),
    [source, orgId, filterProjectId, filters.model, filters.capability, filters.groupBy, filters.search,
     sort.col, sort.dir, page, dateRange]
  );

  const rows  = result?.data  || [];
  const total = result?.total || 0;

  return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)', maxWidth: 1400 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 16 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.3px' }}>
            {t('usage_title')}
          </h1>
          <p style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4, marginBottom: 0 }}>
            {total} {t('records')}
          </p>
        </div>
        {error && (
          <button onClick={reload} style={{ padding: '6px 12px', background: 'var(--accent)', color: 'white',
            border: 'none', borderRadius: 6, cursor: 'pointer', fontSize: 12, fontWeight: 600 }}>
            {t('retry')}
          </button>
        )}
      </div>

      <FilterBar filters={filters} setFilters={setFilters} projectMap={projectMap} source={source} />

      <Card style={{ padding: 0, overflow: 'hidden' }}>
        <UsageTable
          rows={rows} loading={loading} sort={sort} setSort={setSort}
          page={page} setPage={setPage} pageSize={pageSize} total={total}
        />
      </Card>
    </div>
  );
}

Object.assign(window, { UsagePage });
