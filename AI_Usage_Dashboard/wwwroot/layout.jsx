// layout.jsx — Sidebar, Header, shared layout primitives (i18n-aware)

function Icon({ path, size = 18, className = '' }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
      stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"
      className={className}>
      <path d={path} />
    </svg>
  );
}

function useT() {
  const lang = React.useContext(window.LangContext);
  return k => (window.TRANSLATIONS[lang] || window.TRANSLATIONS.zh)[k] ?? k;
}

function Sidebar({ currentPage, onNav, collapsed }) {
  const t = useT();
  const NAV_ITEMS = [
    { id: 'overview',    label: t('nav_overview'),    icon: 'M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6' },
    { id: 'deprecated',  label: t('nav_deprecated'),  icon: 'M12 9v2m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z' },
    { id: 'usage',       label: t('nav_usage'),       icon: 'M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z' },
    { id: 'cost',        label: t('nav_cost'),        icon: 'M9 7h6m0 10v-3m-3 3h.01M9 17h.01M9 11h.01M12 11h.01M15 11h.01M4 19h16a2 2 0 002-2V7a2 2 0 00-2-2H4a2 2 0 00-2 2v10a2 2 0 002 2z' },
    { id: 'azure',       label: t('nav_azure'),       icon: 'M3 6h18M7 12h10M10 18h4' },
  ];

  const SB = {
    bg:      '#16213E',
    border:  'rgba(255,255,255,0.07)',
    text:    'rgba(255,255,255,0.55)',
    hover:   'rgba(255,255,255,0.07)',
    activeBg:'rgba(255,255,255,0.12)',
    activeText: '#FFFFFF',
  };

  return (
    <aside style={{
      width: collapsed ? 56 : 220, minWidth: collapsed ? 56 : 220,
      height: '100vh', background: SB.bg, borderRight: `1px solid ${SB.border}`,
      display: 'flex', flexDirection: 'column',
      transition: 'width 0.2s ease, min-width 0.2s ease',
      overflow: 'hidden', position: 'sticky', top: 0, zIndex: 30,
    }}>
      <div style={{ padding: collapsed ? '18px 0' : '18px 16px', display: 'flex', alignItems: 'center',
        gap: 10, borderBottom: `1px solid ${SB.border}`, minHeight: 60 }}>
        <div style={{ width: 28, height: 28, minWidth: 28, background: 'var(--accent)',
          borderRadius: 8, display: 'flex', alignItems: 'center', justifyContent: 'center',
          marginLeft: collapsed ? 14 : 0 }}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2.2">
            <path d="M13 10V3L4 14h7v7l9-11h-7z" />
          </svg>
        </div>
        {!collapsed && (
          <div>
            <div style={{ fontSize: 13, fontWeight: 700, color: '#FFFFFF', letterSpacing: '-0.3px' }}>AI Usage</div>
            <div style={{ fontSize: 10, color: SB.text, marginTop: 1 }}>{t('app_subtitle')}</div>
          </div>
        )}
      </div>

      <nav style={{ flex: 1, padding: '8px 0', overflowY: 'auto' }}>
        {NAV_ITEMS.map(item => {
          const active = currentPage === item.id;
          return (
            <button key={item.id} onClick={() => onNav(item.id)} style={{
              display: 'flex', alignItems: 'center', gap: 10,
              width: '100%', padding: collapsed ? '10px 0' : '9px 14px',
              justifyContent: collapsed ? 'center' : 'flex-start',
              background: active ? SB.activeBg : 'transparent',
              color: active ? SB.activeText : SB.text,
              border: 'none', cursor: 'pointer', borderRadius: 0,
              fontSize: 13, fontWeight: active ? 600 : 400,
              transition: 'all 0.12s',
              borderLeft: active ? '2px solid var(--accent)' : `2px solid transparent`,
            }}
            onMouseEnter={e => { if (!active) e.currentTarget.style.background = SB.hover; }}
            onMouseLeave={e => { if (!active) e.currentTarget.style.background = 'transparent'; }}>
              <Icon path={item.icon} size={16} />
              {!collapsed && <span>{item.label}</span>}
            </button>
          );
        })}
      </nav>

    </aside>
  );
}

function Select({ label, value, options, onChange, minWidth = 130, disabled = false }) {
  const [open, setOpen] = React.useState(false);
  const ref = React.useRef(null);
  const normalizedOptions = (options || []).map(opt =>
    (typeof opt === 'object' && opt !== null)
      ? { value: opt.value, label: opt.label ?? String(opt.value ?? '') }
      : { value: opt, label: String(opt ?? '') }
  );
  const selected = normalizedOptions.find(opt => opt.value === value);
  const selectedLabel = selected ? selected.label : String(value ?? '');
  React.useEffect(() => {
    function handler(e) { if (ref.current && !ref.current.contains(e.target)) setOpen(false); }
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);
  return (
    <div ref={ref} style={{ position: 'relative' }}>
      <button onClick={() => !disabled && setOpen(o => !o)} disabled={disabled} style={{
        display: 'flex', alignItems: 'center', gap: 6, padding: '6px 10px',
        background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 7,
        cursor: disabled ? 'not-allowed' : 'pointer', fontSize: 12, color: 'var(--text-secondary)', minWidth,
        fontWeight: 500, opacity: disabled ? 0.55 : 1,
      }}>
        {label && <span style={{ color: 'var(--text-muted)', fontSize: 11 }}>{label}:</span>}
        <span style={{ flex: 1, textAlign: 'left', color: 'var(--text-primary)', fontWeight: 600 }}>
          {selectedLabel || '—'}
        </span>
        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M19 9l-7 7-7-7" />
        </svg>
      </button>
      {open && !disabled && (
        <div style={{ position: 'absolute', top: '100%', left: 0, marginTop: 4, minWidth: '100%',
          background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 8,
          boxShadow: 'var(--shadow-lg)', zIndex: 100, overflow: 'hidden' }}>
          {normalizedOptions.map(opt => (
            <button key={String(opt.value)} onClick={() => { onChange(opt.value); setOpen(false); }} style={{
              display: 'block', width: '100%', textAlign: 'left', padding: '8px 12px',
              background: opt.value === value ? 'var(--accent-subtle)' : 'transparent',
              color: opt.value === value ? 'var(--accent)' : 'var(--text-secondary)',
              border: 'none', cursor: 'pointer', fontSize: 12, fontWeight: opt.value === value ? 600 : 400,
            }}
            onMouseEnter={e => { if (opt.value !== value) e.currentTarget.style.background = 'var(--hover-bg)'; }}
            onMouseLeave={e => { if (opt.value !== value) e.currentTarget.style.background = 'transparent'; }}>
              {opt.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function DateRangePicker({ value, onChange }) {
  const t = useT();
  const [open, setOpen] = React.useState(false);
  const [showCustom, setShowCustom] = React.useState(false);
  const ref = React.useRef(null);
  // Period state holds backend enums; t(...) is only used when rendering labels
  // (architecture principle ③: components own enums, never i18n strings as state).
  const presets = [
    { value: 'today', label: t('date_today')  },
    { value: '7d',    label: t('date_7d')     },
    { value: '30d',   label: t('date_30d')    },
    { value: 'MTD',   label: t('date_mtd')    },
    { value: 'custom', label: t('date_custom') }
  ];
  const [customStart, setCustomStart] = React.useState('');
  const [customEnd, setCustomEnd] = React.useState('');

  const today = React.useMemo(() => {
    const d = new Date();
    const pad = n => String(n).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  }, []);

  React.useEffect(() => {
    if (typeof value === 'string' && value.startsWith('__custom__|')) {
      const parts = value.split('|');
      setCustomStart(parts[1] || today);
      setCustomEnd(parts[2] || today);
      setShowCustom(true);
    } else {
      setShowCustom(false);
    }
  }, [value, today]);

  const displayValue = React.useMemo(() => {
    if (typeof value === 'string' && value.startsWith('__custom__|')) {
      const parts = value.split('|');
      const s = parts[1] || '';
      const e = parts[2] || '';
      return (s && e) ? `${s} ~ ${e}` : t('date_custom');
    }
    const hit = presets.find(p => p.value === value);
    return hit ? hit.label : value;
  }, [value, t]);

  function applyCustomRange() {
    const s = customStart || today;
    const e = customEnd || today;
    if (s <= e) onChange(`__custom__|${s}|${e}`);
    else onChange(`__custom__|${e}|${s}`);
    setOpen(false);
  }

  React.useEffect(() => {
    function handler(e) { if (ref.current && !ref.current.contains(e.target)) setOpen(false); }
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);
  return (
    <div ref={ref} style={{ position: 'relative' }}>
      <button onClick={() => setOpen(o => !o)} style={{
        display: 'flex', alignItems: 'center', gap: 6, padding: '6px 10px',
        background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 7,
        cursor: 'pointer', fontSize: 12, color: 'var(--text-primary)', fontWeight: 600,
      }}>
        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
          <line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" />
        </svg>
        {displayValue}
        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M19 9l-7 7-7-7" />
        </svg>
      </button>
      {open && (
        <div style={{ position: 'absolute', top: '100%', right: 0, marginTop: 4, width: showCustom ? 250 : 180,
          background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 8,
          boxShadow: 'var(--shadow-lg)', zIndex: 100, overflow: 'hidden' }}>
          {!showCustom && presets.map(p => (
            <button key={p.value} onClick={() => {
              if (p.value === 'custom') {
                setShowCustom(true);
                setCustomStart(today);
                setCustomEnd(today);
                return;
              }
              onChange(p.value);
              setOpen(false);
            }} style={{
              display: 'block', width: '100%', textAlign: 'left', padding: '8px 12px',
              background: p.value === value ? 'var(--accent-subtle)' : 'transparent',
              color: p.value === value ? 'var(--accent)' : 'var(--text-secondary)',
              border: 'none', cursor: 'pointer', fontSize: 12, fontWeight: p.value === value ? 600 : 400,
            }}
            onMouseEnter={e => { if (p.value !== value) e.currentTarget.style.background = 'var(--hover-bg)'; }}
            onMouseLeave={e => { if (p.value !== value) e.currentTarget.style.background = 'transparent'; }}>
              {p.label}
            </button>
          ))}
          {showCustom && (
            <div style={{ padding: 10 }}>
              <div style={{ fontSize: 11, color: 'var(--text-muted)', marginBottom: 6 }}>{t('date_start')}</div>
              <input type="date" value={customStart} onChange={e => setCustomStart(e.target.value)}
                style={{ width: '100%', marginBottom: 8, padding: '6px 8px', borderRadius: 6, border: '1px solid var(--border)', background: 'var(--card-bg)', color: 'var(--text-primary)', fontSize: 12 }} />
              <div style={{ fontSize: 11, color: 'var(--text-muted)', marginBottom: 6 }}>{t('date_end')}</div>
              <input type="date" value={customEnd} onChange={e => setCustomEnd(e.target.value)}
                style={{ width: '100%', marginBottom: 10, padding: '6px 8px', borderRadius: 6, border: '1px solid var(--border)', background: 'var(--card-bg)', color: 'var(--text-primary)', fontSize: 12 }} />
              <div style={{ display: 'flex', gap: 6 }}>
                <button onClick={() => setShowCustom(false)} style={{
                  flex: 1, padding: '6px 8px', borderRadius: 6, border: '1px solid var(--border)',
                  background: 'var(--card-bg)', color: 'var(--text-secondary)', cursor: 'pointer', fontSize: 12
                }}>{t('cancel')}</button>
                <button onClick={applyCustomRange} style={{
                  flex: 1, padding: '6px 8px', borderRadius: 6, border: 'none',
                  background: 'var(--accent)', color: 'white', cursor: 'pointer', fontSize: 12, fontWeight: 600
                }}>{t('date_apply')}</button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function LangToggle({ lang, setLang }) {
  return (
    <button onClick={() => setLang(l => l === 'zh' ? 'en' : 'zh')} style={{
      background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 7,
      cursor: 'pointer', padding: '5px 10px', fontSize: 11, fontWeight: 700,
      color: 'var(--text-primary)', letterSpacing: '0.3px', display: 'flex', alignItems: 'center', gap: 5,
      transition: 'border-color 0.15s',
    }}
    onMouseEnter={e => e.currentTarget.style.borderColor = 'var(--accent)'}
    onMouseLeave={e => e.currentTarget.style.borderColor = 'var(--border)'}>
      <span style={{ opacity: lang === 'en' ? 1 : 0.4, transition: 'opacity 0.15s' }}>EN</span>
      <span style={{ color: 'var(--border)', fontWeight: 400 }}>|</span>
      <span style={{ opacity: lang === 'zh' ? 1 : 0.4, transition: 'opacity 0.15s' }}>中</span>
    </button>
  );
}

function Header({ org, setOrg, orgList, projectId, setProjectId, source, setSource, dateRange, setDateRange, theme, setTheme, onToggleSidebar, lang, setLang, projectOptions, textScale, setTextScale }) {
  const t = useT();
  // orgList from DB; each item is { orgId, orgName }
  const orgsRaw = orgList && orgList.length > 0
    ? orgList
    : (org ? [{ orgId: org, orgName: org }] : []);
  const allAccountsLabel = t('all_accounts');
  const orgOptions = source === 'azure'
    ? [{ value: '', label: allAccountsLabel }, ...orgsRaw.map(x => ({ value: x.orgId, label: x.orgName || x.orgId }))]
    : orgsRaw.map(x => ({ value: x.orgId, label: x.orgName || x.orgId }));
  const projects = [{ value: '', label: t('all_projects') }, ...(projectOptions || [])];
  const sourceOptions = [t('source_all'), t('source_openai'), t('source_azure')];
  const sourceValue = source === 'openai' ? t('source_openai') : source === 'azure' ? t('source_azure') : t('source_all');
  const selectorsEnabled = source !== 'all';
  const orgLabel = source === 'azure' ? t('label_subscription') : t('label_org');
  const projectLabel = source === 'azure' ? t('label_service') : t('label_project');
  const orgValue = org || '';
  return (
    <header style={{
      minHeight: 56, display: 'flex', alignItems: 'center', flexWrap: 'wrap',
      gap: 8, padding: '8px clamp(12px, 2vw, 20px)',
      background: 'var(--header-bg)', borderBottom: '1px solid var(--border)',
      position: 'sticky', top: 0, zIndex: 20, backdropFilter: 'blur(8px)',
    }}>
      <button onClick={onToggleSidebar} style={{ background: 'none', border: 'none', cursor: 'pointer',
        color: 'var(--text-muted)', padding: 4, borderRadius: 4, marginRight: 4 }}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <line x1="3" y1="12" x2="21" y2="12" /><line x1="3" y1="6" x2="21" y2="6" /><line x1="3" y1="18" x2="21" y2="18" />
        </svg>
      </button>

      <Select label={t('label_source')} value={sourceValue} options={sourceOptions}
        onChange={v => setSource(v === t('source_openai') ? 'openai' : v === t('source_azure') ? 'azure' : 'all')} minWidth={130} />
      <Select
        label={orgLabel}
        value={orgValue}
        options={orgOptions}
        onChange={v => setOrg(v || '')}
        minWidth={160}
        disabled={!selectorsEnabled}
      />
      <Select
        label={projectLabel}
        value={projectId || ''}
        options={projects}
        onChange={v => setProjectId(v || '')}
        minWidth={170}
        disabled={!selectorsEnabled}
      />
      
      <div style={{ flex: 1 }} />

      <div title={t('currency_usd_tag')} style={{
        display: 'inline-flex', alignItems: 'center', gap: 5, padding: '5px 9px', borderRadius: 7,
        background: 'rgba(245,158,11,0.08)', border: '1px solid rgba(245,158,11,0.25)',
        color: '#B45309', fontSize: 11, fontWeight: 700, letterSpacing: '0.3px',
      }}>
        <span style={{ fontSize: 13, lineHeight: 1 }}>💵</span>
        <span>{t('currency_usd_chip')}</span>
      </div>

      <DateRangePicker value={dateRange} onChange={setDateRange} />

      <LangToggle lang={lang} setLang={setLang} />
      <button onClick={() => setTextScale(v => Math.min(3, Number((v + 0.12).toFixed(2))))} title="A+" style={{
        background: textScale > 1 ? 'var(--accent-subtle)' : 'var(--card-bg)',
        border: `1px solid ${textScale > 1 ? 'var(--accent)' : 'var(--border)'}`,
        borderRadius: 7, cursor: 'pointer', padding: '6px 10px',
        color: textScale > 1 ? 'var(--accent)' : 'var(--text-muted)',
        display: 'flex', alignItems: 'center', gap: 6, fontSize: 11, fontWeight: 700,
      }}>
        <span>A+</span>
      </button>

      <button onClick={() => setTextScale(v => Math.max(1, Number((v - 0.12).toFixed(2))))} title="A-" disabled={textScale <= 1} style={{
        background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 7,
        cursor: textScale <= 1 ? 'not-allowed' : 'pointer', padding: '6px 10px',
        color: 'var(--text-muted)', fontSize: 11, fontWeight: 700, opacity: textScale <= 1 ? 0.45 : 1,
      }}>
        A-
      </button>

      <button onClick={() => setTheme(t2 => t2 === 'light' ? 'dark' : 'light')} style={{
        background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 7,
        cursor: 'pointer', padding: '6px 8px', color: 'var(--text-muted)', display: 'flex',
      }}>
        {theme === 'light'
          ? <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><path d="M21 12.79A9 9 0 1111.21 3 7 7 0 0021 12.79z" /></svg>
          : <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/></svg>
        }
      </button>

    </header>
  );
}

function Card({ children, style = {}, onClick }) {
  return (
    <div onClick={onClick} style={{
      background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 10,
      boxShadow: 'var(--shadow-sm)', padding: 20, ...style,
      cursor: onClick ? 'pointer' : undefined,
    }}>
      {children}
    </div>
  );
}

function Skeleton({ width = '100%', height = 16, radius = 4 }) {
  return (
    <div style={{ width, height, borderRadius: radius,
      background: 'var(--skeleton)', animation: 'pulse 1.4s ease-in-out infinite' }} />
  );
}

function Badge({ label, color }) {
  const colors = {
    blue:   { bg: 'var(--accent-subtle)', text: 'var(--accent)' },
    green:  { bg: 'rgba(16,185,129,0.1)', text: '#059669' },
    amber:  { bg: 'rgba(245,158,11,0.12)', text: '#D97706' },
    red:    { bg: 'rgba(239,68,68,0.1)', text: '#DC2626' },
    gray:   { bg: 'var(--hover-bg)', text: 'var(--text-muted)' },
  };
  const c = colors[color] || colors.gray;
  return (
    <span style={{ display: 'inline-block', padding: '2px 7px', borderRadius: 4,
      fontSize: 11, fontWeight: 600, background: c.bg, color: c.text }}>{label}</span>
  );
}

function EmptyState({ title, description, icon }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '48px 24px',
      color: 'var(--text-muted)', textAlign: 'center' }}>
      <div style={{ width: 48, height: 48, borderRadius: 12, background: 'var(--hover-bg)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', marginBottom: 16 }}>
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
          <path d={icon || 'M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z'} />
        </svg>
      </div>
      <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-secondary)', marginBottom: 6 }}>{title}</div>
      <div style={{ fontSize: 12 }}>{description}</div>
    </div>
  );
}

function ErrorState({ onRetry }) {
  const t = useT();
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', padding: '48px 24px',
      color: 'var(--text-muted)', textAlign: 'center' }}>
      <div style={{ fontSize: 14, fontWeight: 600, color: '#EF4444', marginBottom: 8 }}>{t('error_title')}</div>
      <div style={{ fontSize: 12, marginBottom: 16 }}>{t('error_desc')}</div>
      <button onClick={onRetry} style={{ padding: '7px 16px', background: 'var(--accent)', color: 'white',
        border: 'none', borderRadius: 6, cursor: 'pointer', fontSize: 12, fontWeight: 600 }}>{t('retry')}</button>
    </div>
  );
}

Object.assign(window, { Sidebar, Header, Card, Skeleton, Badge, EmptyState, ErrorState, Select, DateRangePicker, LangToggle, useT, Icon });
