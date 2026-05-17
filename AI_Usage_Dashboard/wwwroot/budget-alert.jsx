// budget-alert.jsx — Budget & Alerts page (i18n-aware, real API)

function BudgetGauge({ pct, level }) {
  const colors = { ok: '#10B981', warning: '#F59E0B', high: '#F97316', critical: '#EF4444' };
  const color = colors[level] || colors.ok;
  const clamped = Math.min(pct, 100);
  return (
    <div style={{ position: 'relative', width: 56, height: 56 }}>
      <svg viewBox="0 0 36 36" width="56" height="56" style={{ transform: 'rotate(-90deg)' }}>
        <circle cx="18" cy="18" r="14" fill="none" stroke="var(--border)" strokeWidth="3.5" />
        <circle cx="18" cy="18" r="14" fill="none" stroke={color} strokeWidth="3.5"
          strokeDasharray={`${clamped * 0.879} 87.9`}
          strokeLinecap="round" style={{ transition: 'stroke-dasharray 0.6s ease' }} />
      </svg>
      <div style={{ position: 'absolute', inset: 0, display: 'flex', alignItems: 'center',
        justifyContent: 'center', fontSize: 9.5, fontWeight: 700, color }}>
        {pct.toFixed(0)}%
      </div>
    </div>
  );
}

function LevelBadge({ level }) {
  const t = useT();
  const cfg = {
    ok:       { key: 'level_ok',       bg: 'rgba(16,185,129,0.1)',  color: '#059669' },
    warning:  { key: 'level_warning',  bg: 'rgba(245,158,11,0.12)', color: '#D97706' },
    high:     { key: 'level_high',     bg: 'rgba(249,115,22,0.12)', color: '#EA580C' },
    critical: { key: 'level_critical', bg: 'rgba(239,68,68,0.1)',   color: '#DC2626' },
  };
  const c = cfg[level] || cfg.ok;
  return (
    <span style={{ display: 'inline-block', padding: '3px 8px', borderRadius: 5,
      fontSize: 11, fontWeight: 700, background: c.bg, color: c.color }}>{t(c.key)}</span>
  );
}

function BudgetRow({ b, onEdit }) {
  const t = useT();
  const barColor = b.level === 'critical' ? '#EF4444' : b.level === 'high' ? '#F97316'
    : b.level === 'warning' ? '#F59E0B' : '#10B981';
  return (
    <tr onMouseEnter={e => e.currentTarget.style.background = 'var(--hover-bg)'}
        onMouseLeave={e => e.currentTarget.style.background = 'transparent'}>
      <td style={{ padding: '14px 16px', borderBottom: '1px solid var(--border)' }}>
        <div style={{ fontWeight: 600, fontSize: 13, color: 'var(--text-primary)' }}>{b.project}</div>
        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>{t('monthly_budget_lbl')}</div>
      </td>
      <td style={{ padding: '14px 16px', borderBottom: '1px solid var(--border)', textAlign: 'right',
        fontVariantNumeric: 'tabular-nums' }}>
        <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)' }}>${b.monthlyBudget.toFixed(0)}</div>
      </td>
      <td style={{ padding: '14px 16px', borderBottom: '1px solid var(--border)', textAlign: 'right',
        fontVariantNumeric: 'tabular-nums' }}>
        <div style={{ fontSize: 13, fontWeight: 700, color: barColor }}>${b.spent.toFixed(2)}</div>
        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 1 }}>
          ${b.remaining.toFixed(2)} {t('remaining')}
        </div>
      </td>
      <td style={{ padding: '14px 24px', borderBottom: '1px solid var(--border)', minWidth: 180 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          <BudgetGauge pct={b.pct} level={b.level} />
          <div style={{ flex: 1 }}>
            <div style={{ height: 6, background: 'var(--border)', borderRadius: 3, overflow: 'hidden', marginBottom: 4 }}>
              <div style={{ width: `${Math.min(b.pct, 100)}%`, height: '100%', background: barColor,
                borderRadius: 3, transition: 'width 0.6s ease',
                boxShadow: b.pct > 90 ? `0 0 6px ${barColor}88` : 'none' }} />
            </div>
            <div style={{ position: 'relative', height: 8 }}>
              {[80, 90, 100].map(th => (
                <div key={th} style={{ position: 'absolute', left: `${th}%`, transform: 'translateX(-50%)',
                  fontSize: 9, color: 'var(--text-muted)', top: 0 }}>{th}</div>
              ))}
            </div>
          </div>
        </div>
      </td>
      <td style={{ padding: '14px 16px', borderBottom: '1px solid var(--border)', textAlign: 'center' }}>
        <LevelBadge level={b.level} />
      </td>
      <td style={{ padding: '14px 16px', borderBottom: '1px solid var(--border)', textAlign: 'right' }}>
        <button onClick={() => onEdit(b)} style={{ padding: '5px 10px', fontSize: 11, fontWeight: 600,
          background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 6,
          color: 'var(--text-secondary)', cursor: 'pointer' }}
          onMouseEnter={e => e.currentTarget.style.borderColor = 'var(--accent)'}
          onMouseLeave={e => e.currentTarget.style.borderColor = 'var(--border)'}>
          {t('edit_budget')}
        </button>
      </td>
    </tr>
  );
}

function AlertHistoryRow({ a }) {
  const icons = {
    warning:  { color: '#D97706', bg: 'rgba(245,158,11,0.1)',  path: 'M12 9v2m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z' },
    high:     { color: '#EA580C', bg: 'rgba(249,115,22,0.1)',  path: 'M12 9v2m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z' },
    critical: { color: '#DC2626', bg: 'rgba(239,68,68,0.1)',   path: 'M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z' },
  };
  const ic = icons[a.level] || icons.warning;
  return (
    <div style={{ display: 'flex', alignItems: 'flex-start', gap: 12, padding: '12px 0',
      borderBottom: '1px solid var(--border)' }}>
      <div style={{ width: 32, height: 32, borderRadius: 8, background: ic.bg, minWidth: 32,
        display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke={ic.color} strokeWidth="2">
          <path d={ic.path} />
        </svg>
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-primary)' }}>{a.message}</div>
        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 2 }}>
          <span style={{ color: 'var(--text-secondary)', fontWeight: 500 }}>{a.project}</span>
          {' · '}{a.ts}
        </div>
      </div>
      <LevelBadge level={a.level} />
    </div>
  );
}

function EditBudgetModal({ budget, onClose, onSave, saving }) {
  const t = useT();
  const [value, setValue] = React.useState(String(budget.monthlyBudget));
  return (
    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.45)', zIndex: 200,
      display: 'flex', alignItems: 'center', justifyContent: 'center' }}
      onClick={onClose}>
      <div style={{ background: 'var(--card-bg)', borderRadius: 12, padding: 24, width: 340,
        border: '1px solid var(--border)', boxShadow: 'var(--shadow-lg)' }}
        onClick={e => e.stopPropagation()}>
        <div style={{ fontSize: 15, fontWeight: 700, color: 'var(--text-primary)', marginBottom: 4 }}>
          {t('edit_budget')}
        </div>
        <div style={{ fontSize: 12, color: 'var(--text-muted)', marginBottom: 18 }}>{budget.project}</div>
        <label style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-muted)', display: 'block',
          marginBottom: 6, textTransform: 'uppercase', letterSpacing: '0.4px' }}>
          {t('budget_usd')}
        </label>
        <div style={{ position: 'relative', marginBottom: 20 }}>
          <span style={{ position: 'absolute', left: 10, top: '50%', transform: 'translateY(-50%)',
            fontSize: 13, color: 'var(--text-muted)' }}>$</span>
          <input type="number" value={value} onChange={e => setValue(e.target.value)}
            style={{ width: '100%', padding: '9px 12px 9px 22px', fontSize: 14, fontWeight: 600,
              border: '1px solid var(--border)', borderRadius: 8, background: 'var(--card-bg)',
              color: 'var(--text-primary)', outline: 'none', boxSizing: 'border-box' }} />
        </div>
        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
          <button onClick={onClose} disabled={saving} style={{ padding: '8px 16px', borderRadius: 7, border: '1px solid var(--border)',
            background: 'transparent', color: 'var(--text-secondary)', fontSize: 12, cursor: 'pointer', fontWeight: 600 }}>
            {t('cancel')}
          </button>
          <button onClick={() => onSave(parseFloat(value))} disabled={saving} style={{ padding: '8px 16px', borderRadius: 7,
            border: 'none', background: 'var(--accent)', color: 'white', fontSize: 12, cursor: 'pointer', fontWeight: 600,
            opacity: saving ? 0.7 : 1 }}>
            {saving ? '…' : t('save_budget')}
          </button>
        </div>
      </div>
    </div>
  );
}

function BudgetPage({ dateRange }) {
  const t = useT();
  const [editing, setEditing] = React.useState(null);
  const [saving,  setSaving]  = React.useState(false);

  const { loading: budgetLoading, data: budgetsResp, reload: reloadBudgets } =
    useAsync(() => window.API.getBudgets(), []);
  const { loading: alertLoading, data: alertsRaw } =
    useAsync(() => window.API.getAlerts(50, dateRange), [dateRange]);

  const budgets   = budgetsResp?.items   || [];
  const summary   = budgetsResp?.summary || { critical: 0, warning: 0, ok: 0 };
  const alerts    = alertsRaw            || [];

  const critCount = summary.critical;
  const warnCount = summary.warning;

  async function handleSave(newBudget) {
    if (!editing) return;
    setSaving(true);
    try {
      await window.API.putBudget(editing.projectId, newBudget);
      reloadBudgets();
      setEditing(null);
    } catch (e) {
      alert('Save failed: ' + e.message);
    } finally {
      setSaving(false);
    }
  }

  const colHeaders = [t('col_project'), t('col_budget'), t('col_spent'), t('col_usage'), t('col_status'), ''];

  return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)', maxWidth: 1200 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 20 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.3px' }}>
            {t('budget_title')}
          </h1>
          <p style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 4, marginBottom: 0 }}>
            {critCount > 0 && <span style={{ color: '#EF4444', fontWeight: 600 }}>{critCount} {t('critical_n')}</span>}
            {warnCount > 0 && <span style={{ color: '#F59E0B', fontWeight: 600 }}>{warnCount} {t('warning_n')}</span>}
            {budgets.length} {t('projects_tracked')}
          </p>
        </div>
        <button style={{ padding: '7px 14px', background: 'var(--accent)', color: 'white', border: 'none',
          borderRadius: 7, cursor: 'pointer', fontSize: 12, fontWeight: 600, display: 'flex',
          alignItems: 'center', gap: 6 }}>
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.5">
            <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
          </svg>
          {t('add_budget')}
        </button>
      </div>

      <Card style={{ padding: 0, marginBottom: 16, overflow: 'hidden' }}>
        {budgetLoading ? (
          <div style={{ padding: 24 }}>
            {[1,2,3].map(i => <div key={i} style={{ marginBottom: 12 }}><Skeleton width="100%" height={48} radius={6} /></div>)}
          </div>
        ) : budgets.length === 0 ? (
          <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />
        ) : (
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead>
              <tr style={{ background: 'var(--table-header)' }}>
                {colHeaders.map((h, i) => (
                  <th key={i} style={{ padding: '10px 16px', fontSize: 11, fontWeight: 600, color: 'var(--text-muted)',
                    textAlign: (h === t('col_budget') || h === t('col_spent')) ? 'right' : h === t('col_status') ? 'center' : 'left',
                    borderBottom: '1px solid var(--border)', textTransform: 'uppercase', letterSpacing: '0.4px' }}>{h}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {budgets.map(b => <BudgetRow key={b.projectId} b={b} onEdit={setEditing} />)}
            </tbody>
          </table>
        )}
      </Card>

      <Card>
        <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 12 }}>
          {t('alert_history')}
        </div>
        {alertLoading
          ? [1,2,3].map(i => <div key={i} style={{ marginBottom: 10 }}><Skeleton width="100%" height={44} radius={6} /></div>)
          : alerts.length === 0
            ? <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />
            : alerts.map(a => <AlertHistoryRow key={a.id} a={a} />)
        }
      </Card>

      {editing && (
        <EditBudgetModal
          budget={editing}
          onClose={() => !saving && setEditing(null)}
          onSave={handleSave}
          saving={saving}
        />
      )}
    </div>
  );
}

Object.assign(window, { BudgetPage });
