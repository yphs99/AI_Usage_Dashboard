// azure-page.jsx — Azure data page (DB-cached)

function AzurePage({ dateRange, orgId, projectId, source }) {
  const [dataset, setDataset] = React.useState('accounts');
  const [page, setPage] = React.useState(1);
  const [pageSize] = React.useState(15);
  const [search, setSearch] = React.useState('');

  // header dropdowns: in Azure context, orgId = subscriptionId, projectId = accountName.
  // When source='openai' the page is irrelevant so we ignore the filters; the table still
  // shows raw azure data (the user can switch source to filter).
  const subId       = source === 'openai' ? '' : (orgId || '');
  const acctName    = source === 'openai' ? '' : (projectId || '');
  const periodParam = dateRange || 'MTD';

  React.useEffect(() => { setPage(1); }, [dataset, search, subId, acctName, periodParam]);

  const { data: statusData } =
    useAsync(() => window.API.getAzureStatus(subId), [subId]);

  const { loading: qLoading, data: queryData, error: qErr } =
    useAsync(() => window.API.getAzureQuery(dataset, {
      page, pageSize, search,
      subscriptionId: subId, accountName: acctName, period: periodParam,
    }), [dataset, page, pageSize, search, subId, acctName, periodParam]);

  const dsOptions = [
    { id: 'accounts', label: 'Azure 帳戶' },
    { id: 'deployments', label: '部署' },
    { id: 'modelUsage', label: '模型用量' },
  ];

  const columns = dataset === 'deployments'
    ? ['subscriptionName', 'resourceGroup', 'accountName', 'deploymentName', 'modelName', 'modelVersion', 'region', 'status', 'provisioningState']
    : dataset === 'modelUsage'
      ? ['subscriptionName', 'accountName', 'deploymentName', 'modelName', 'requests', 'inputTokens', 'outputTokens', 'dateUtc']
      : ['subscriptionName', 'resourceGroup', 'accountName', 'kind', 'region', 'scanStatus'];

  const rows = queryData?.data || [];
  const total = queryData?.total || 0;
  const totalPages = Math.max(1, queryData?.totalPages || 1);

  function headerLabel(k) {
    const map = {
      subscriptionName: '訂閱',
      resourceGroup: '資源群組',
      accountName: '帳戶',
      kind: '服務',
      region: '區域',
      scanStatus: '狀態',
      deploymentName: '部署',
      modelName: '模型',
      modelVersion: '版本',
      status: '部署狀態',
      provisioningState: '佈建狀態',
      requests: '請求數',
      inputTokens: '輸入 Tokens',
      outputTokens: '輸出 Tokens',
      dateUtc: '日期(UTC)',
    };
    return map[k] || k;
  }

  const counts = statusData?.counts || {};

  return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)', maxWidth: 1400 }}>
      <div style={{ marginBottom: 14 }}>
        <h1 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.3px' }}>
          Azure 資料
        </h1>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(140px, 1fr))', gap: 10, marginBottom: 14 }}>
        {[
          ['AI 帳戶', counts.azure_accounts_raw || 0],
          ['部署', counts.azure_deployments_raw || 0],
          ['訂閱', counts.azure_subscriptions_raw || 0],
        ].map(([k, v]) => (
          <Card key={k} style={{ padding: 12 }}>
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>{k}</div>
            <div style={{ fontSize: 22, fontWeight: 700, color: 'var(--text-primary)', marginTop: 2 }}>{v}</div>
          </Card>
        ))}
      </div>

      <Card style={{ padding: 0, overflow: 'hidden' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '12px 14px', borderBottom: '1px solid var(--border)' }}>
          <div style={{ display: 'flex', gap: 6 }}>
            {dsOptions.map(x => (
              <button key={x.id} onClick={() => setDataset(x.id)} style={{
                padding: '6px 10px', borderRadius: 7, border: '1px solid var(--border)',
                background: dataset === x.id ? 'var(--accent-subtle)' : 'var(--card-bg)',
                color: dataset === x.id ? 'var(--accent)' : 'var(--text-secondary)',
                fontWeight: dataset === x.id ? 600 : 500, fontSize: 12, cursor: 'pointer'
              }}>{x.label}</button>
            ))}
          </div>
          <input
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder="搜尋..."
            style={{
              padding: '6px 10px', borderRadius: 7, border: '1px solid var(--border)', width: 220,
              fontSize: 12, color: 'var(--text-primary)', background: 'var(--card-bg)'
            }}
          />
        </div>

        {qErr && <div style={{ padding: 14, fontSize: 12, color: '#DC2626' }}>Azure 清單載入失敗。</div>}
        {qLoading && <div style={{ padding: 14, fontSize: 12, color: 'var(--text-muted)' }}>載入中…</div>}

        {!qLoading && !qErr && (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 900 }}>
              <thead>
                <tr>
                  {columns.map(c => (
                    <th key={c} style={{ textAlign: 'left', padding: '8px 10px', fontSize: 11, color: 'var(--text-muted)', borderBottom: '1px solid var(--border)', background: 'var(--table-header)' }}>
                      {headerLabel(c)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((r, i) => (
                  <tr key={i}
                    style={{ background: i % 2 === 1 ? 'var(--table-stripe)' : 'transparent' }}
                    onMouseEnter={e => e.currentTarget.style.background = 'var(--hover-bg)'}
                    onMouseLeave={e => e.currentTarget.style.background = i % 2 === 1 ? 'var(--table-stripe)' : 'transparent'}>
                    {columns.map(c => (
                      <td key={c} style={{ padding: '8px 10px', fontSize: 12, color: 'var(--text-secondary)', borderBottom: '1px solid var(--border)', whiteSpace: 'nowrap' }}>
                        {c === 'dateUtc' && r[c] ? r[c].slice(0, 10) : (r[c] || '—')}
                      </td>
                    ))}
                  </tr>
                ))}
                {!rows.length && (
                  <tr><td colSpan={columns.length} style={{ padding: 18, fontSize: 12, color: 'var(--text-muted)' }}>無資料</td></tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 14px' }}>
          <div style={{ fontSize: 12, color: 'var(--text-muted)' }}>共 {total} 筆</div>
          <div style={{ display: 'flex', gap: 6 }}>
            <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page <= 1}
              style={{ padding: '5px 10px', borderRadius: 6, border: '1px solid var(--border)', background: 'var(--card-bg)', cursor: 'pointer', opacity: page <= 1 ? 0.4 : 1 }}>
              上一頁
            </button>
            <div style={{ fontSize: 12, color: 'var(--text-secondary)', display: 'flex', alignItems: 'center' }}>{page}/{totalPages}</div>
            <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page >= totalPages}
              style={{ padding: '5px 10px', borderRadius: 6, border: '1px solid var(--border)', background: 'var(--card-bg)', cursor: 'pointer', opacity: page >= totalPages ? 0.4 : 1 }}>
              下一頁
            </button>
          </div>
        </div>
      </Card>
    </div>
  );
}

Object.assign(window, { AzurePage });
