// overview.jsx — Overview page (i18n-aware, real API)

function fmt$(v) { return v >= 1000 ? `$${(v/1000).toFixed(2)}k` : `$${v.toFixed(2)}`; }
function fmtN(v) { return v >= 1e9 ? `${(v/1e9).toFixed(2)}B` : v >= 1e6 ? `${(v/1e6).toFixed(1)}M` : v >= 1e3 ? `${(v/1e3).toFixed(1)}K` : String(v); }
function fmtFull$(v) {
  const n = Number(v || 0);
  return `$${n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

class ChartErrorBoundary extends React.Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false };
  }
  static getDerivedStateFromError() {
    return { hasError: true };
  }
  componentDidCatch(err) {
    try { console.error('Trend chart render failed:', err); } catch {}
  }
  render() {
    if (this.state.hasError) {
      return (
        <div style={{ marginTop: 16 }}>
          <ErrorState
            title="Trend chart render error"
            description="The chart failed to render. Please refresh or switch chart mode."
          />
        </div>
      );
    }
    return this.props.children;
  }
}

function DeltaBadge({ delta }) {
  if (delta === undefined || delta === null) return null;
  const lang = React.useContext(window.LangContext);
  const up = delta >= 0;

  // en: green = up (cost rose), red = down — Western convention
  // zh: red  = up (cost rose), green = down — East-Asian convention
  const upColor   = lang === 'en' ? '#10B981' : '#EF4444';
  const downColor = lang === 'en' ? '#EF4444' : '#10B981';
  const upBg      = lang === 'en' ? 'rgba(16,185,129,0.1)' : 'rgba(239,68,68,0.1)';
  const downBg    = lang === 'en' ? 'rgba(239,68,68,0.1)' : 'rgba(16,185,129,0.1)';

  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 2, fontSize: 11, fontWeight: 600,
      color: up ? upColor : downColor, background: up ? upBg : downBg,
      padding: '2px 6px', borderRadius: 4 }}>
      <svg width="9" height="9" viewBox="0 0 10 10" fill="currentColor">
        {up ? <polygon points="5,1 9,8 1,8" /> : <polygon points="5,9 9,2 1,2" />}
      </svg>
      {Math.abs(delta).toFixed(1)}%
    </span>
  );
}

function StatCard({ label, value, delta, sub, loading, color = 'var(--accent)', sparkData }) {
  if (loading) return (
    <Card>
      <Skeleton width="60%" height={12} /><br/>
      <Skeleton width="80%" height={28} style={{ margin: '10px 0 8px' }} />
      <Skeleton width="40%" height={10} />
    </Card>
  );
  return (
    <Card style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <div style={{ fontSize: 11, color: 'var(--text-muted)', fontWeight: 500, letterSpacing: '0.3px',
        textTransform: 'uppercase' }}>{label}</div>
      <div style={{ display: 'flex', alignItems: 'stretch', gap: 8, marginTop: 4 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <div style={{ fontSize: 26, fontWeight: 700, color: 'var(--text-primary)', letterSpacing: '-0.5px',
            lineHeight: 1 }}>{value}</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            {delta !== undefined && <DeltaBadge delta={delta} />}
            {sub && <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>{sub}</span>}
          </div>
        </div>
        {sparkData && sparkData.length > 1 && (
          <div style={{ flex: 1, minWidth: 0, display: 'flex', alignItems: 'flex-end' }}>
            <SparkLine values={sparkData} color={color} width="100%" height={48} />
          </div>
        )}
      </div>
    </Card>
  );
}

function TrendLinePage({ data, loading, largeText = false }) {
  const t = useT();
  const [hovered, setHovered] = React.useState(null);
  const scrollRef = React.useRef(null);
  const [scrollWidth, setScrollWidth] = React.useState(0);
  const MAX_VISIBLE_CHART_WIDTH = 1280;
  const hasLongRange = (data?.length || 0) > 30;
  const trendChartHeight = largeText ? 380 : 340;
  const pointWidth = hasLongRange ? 24 : 18;

  // For long-range only: measure container to know the scrollable viewport width
  React.useLayoutEffect(() => {
    if (!hasLongRange) return;
    const update = () => {
      if (!scrollRef.current) return;
      const w = Math.floor(scrollRef.current.clientWidth || 0);
      if (w > 0) setScrollWidth(w);
    };
    update();
    window.addEventListener('resize', update);
    return () => window.removeEventListener('resize', update);
  }, [hasLongRange]);

  React.useEffect(() => {
    if (!hasLongRange || !scrollRef.current) return;
    scrollRef.current.scrollLeft = Math.max(0, scrollRef.current.scrollWidth - scrollRef.current.clientWidth);
  }, [hasLongRange, data?.length]);

  if (loading) return <div style={{ marginTop: 16 }}><Skeleton width="100%" height={trendChartHeight} radius={8} /></div>;
  if (!data || !data.length) return <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />;

  const longRangeWidth = Math.max(scrollWidth || MAX_VISIBLE_CHART_WIDTH, data.length * pointWidth);

  // Compute Y-axis ticks (mirrors LineChart internals) for HTML overlay labels
  const vals = data.map(d => d.cost);
  const minV = Math.min(...vals), maxV = Math.max(...vals);
  const range = maxV - minV || 1;
  const lo = Math.max(0, minV - range * 0.1), hi = maxV + range * 0.1;
  const yTicks = 4;
  const PAD_T = 16, PAD_B = 40;
  const iH = trendChartHeight - PAD_T - PAD_B;
  const toYPx = (v) => PAD_T + (1 - (v - lo) / (hi - lo)) * iH;
  const gridYs = Array.from({ length: yTicks + 1 }, (_, i) => lo + (i / yTicks) * (hi - lo));

  // Always show a label — default to the latest data point when not hovering.
  const display = hovered || data[data.length - 1];

  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
        {display ? (
          <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-primary)',
            background: 'var(--hover-bg)', padding: '4px 10px', borderRadius: 6 }}>
            {display.date} — <span style={{ color: 'var(--accent)', fontFamily: 'JetBrains Mono, monospace' }}>{fmtFull$(display.cost)}</span>
            <span style={{ color: 'var(--text-muted)', fontWeight: 400, marginLeft: 8 }}>
              {fmtN(display.requests)} req
            </span>
          </div>
        ) : <div />}
      </div>
      <div style={{ position: 'relative', paddingLeft: 72 }}>
        {/* HTML Y-axis labels */}
        {gridYs.map((v, i) => (
          <div key={i} style={{
            position: 'absolute', left: 0, top: toYPx(v),
            width: 68, textAlign: 'right', fontSize: 11,
            color: 'var(--text-muted)', transform: 'translateY(-50%)',
            pointerEvents: 'none',
          }}>
            {fmtFull$(v)}
          </div>
        ))}
        <div style={{
          width: '100%',
          overflowX: hasLongRange ? 'auto' : 'hidden',
          overflowY: 'hidden',
          paddingBottom: hasLongRange ? 6 : 0,
        }} ref={scrollRef}>
          {hasLongRange ? (
            <div style={{ width: longRangeWidth }}>
              <LineChart
                data={data} xKey="date" yKey="cost" color="var(--accent)" height={trendChartHeight}
                formatX={(v) => v.slice(5)}
                formatY={(v) => fmtFull$(v)}
                onHover={(d) => setHovered(d)}
                gradientId="trend-main"
                width={longRangeWidth}
                largeText={largeText}
                showYLabels={false}
                minY={0}
              />
            </div>
          ) : (
            <LineChart
              data={data} xKey="date" yKey="cost" color="var(--accent)" height={trendChartHeight}
              formatX={(v) => v.slice(5)}
              formatY={(v) => fmtFull$(v)}
              onHover={(d) => setHovered(d)}
              gradientId="trend-main"
              width={MAX_VISIBLE_CHART_WIDTH}
              largeText={largeText}
              showYLabels={false}
              minY={0}
              showDots
            />
          )}
        </div>
      </div>
      <div style={{
        fontSize: 11, color: 'var(--text-muted)', marginTop: 2, minHeight: 16,
        visibility: hasLongRange ? 'visible' : 'hidden'
      }}>
        {t('trend_scroll_hint')}
      </div>
    </>
  );
}

function TrendStackedPage({ stacked, loading }) {
  const t = useT();
  const [hoverInfo, setHoverInfo] = React.useState(null); // { idx, barCenterX, rootWidth }

  if (loading) return <div style={{ marginTop: 16 }}><Skeleton width="100%" height={360} radius={8} /></div>;
  if (!stacked || !stacked.days || stacked.days.length === 0)
    return <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />;

  const days = stacked.days;
  const series = stacked.series || [];
  const maxDay = Math.max(...days.map(d => Number(d.totalCostUsd || 0)), 1);
  const avgTop = Math.min(100, (Number(stacked.avgDailySpend || 0) / maxDay) * 100);
  const colors = ['#3B82F6','#8B5CF6','#EC4899','#F43F5E','#F97316','#EAB308','#84CC16','#22C55E','#14B8A6','#06B6D4','#64748B'];

  const colorByKey = {};
  series.forEach((s, idx) => { colorByKey[s.key] = colors[idx % colors.length]; });
  const tickStep = Math.max(1, Math.floor(days.length / 6));
  const cellWidth = 34;
  const barWidth = 30;
  const contentWidth = null; // fill container; horizontal scroll only when > container
  const shortDate = (v) => String(v || '').slice(5);
  const hasAnySegment = series.some((s) =>
    days.some((d) => Number((d.bySeries && d.bySeries[s.key]) || 0) > 0)
  );
  if (!hasAnySegment) {
    return <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />;
  }

  const hoverDay = hoverInfo && hoverInfo.idx >= 0 ? days[hoverInfo.idx] : null;
  const hoverDetails = hoverDay
    ? series
      .map(s => ({ key: s.key, label: s.label, cost: Number((hoverDay.bySeries && hoverDay.bySeries[s.key]) || 0) }))
      .filter(x => x.cost > 0)
      .sort((a, b) => b.cost - a.cost)
    : [];

  const tooltipWidth = 360;
  const tooltipX = hoverInfo
    ? (() => {
        const rootWidth = hoverInfo.rootWidth || 420;
        const gap = 16;
        const preferred = hoverInfo.barCenterX < rootWidth / 2
          ? hoverInfo.barCenterX + gap
          : hoverInfo.barCenterX - tooltipWidth - gap;
        return Math.min(Math.max(12, preferred), Math.max(12, rootWidth - tooltipWidth - 12));
      })()
    : 12;
  const tooltipY = 70;

  return (
    <div data-stacked-chart-root style={{ position: 'relative', height: 420, paddingTop: 8, paddingLeft: 72 }}>
      <div style={{
        position: 'absolute', left: 72, right: 0, top: `${Math.max(8, 95 - avgTop)}%`,
        borderTop: '1px dashed rgba(107,114,128,0.5)', pointerEvents: 'none'
      }} />

      <div style={{ position: 'absolute', left: 0, top: `${Math.max(2, 93 - avgTop)}%`, fontSize: 12, color: 'var(--text-muted)', width: 68, textAlign: 'right' }}>
        {fmtFull$(stacked.avgDailySpend || 0)}
      </div>

      <div style={{ overflowX: 'auto', overflowY: 'hidden', paddingTop: 24, paddingBottom: 4 }}>
        <div style={{ minWidth: days.length * cellWidth, width: '100%' }}>
          <div style={{ height: 360, display: 'flex', alignItems: 'stretch' }}>
            {days.map((d, idx) => {
              const total = Number(d.totalCostUsd || 0);
              const heightPct = (total / maxDay) * 100;
              return (
                <div key={`${String(d.date || 'na')}-${idx}`} style={{ flex: 1, minWidth: cellWidth, display: 'flex', justifyContent: 'center', alignItems: 'flex-end' }}>
                  <div
                    onMouseEnter={(e) => {
                      const rootRect = e.currentTarget.closest('[data-stacked-chart-root]')?.getBoundingClientRect();
                      const barRect = e.currentTarget.getBoundingClientRect();
                      setHoverInfo({
                        idx,
                        barCenterX: rootRect ? (barRect.left - rootRect.left + barRect.width / 2) : 0,
                        rootWidth: rootRect ? rootRect.width : 0,
                      });
                    }}
                    onMouseLeave={() => setHoverInfo(null)}
                    style={{ width: '80%', height: `${Math.max(1, heightPct)}%`, display: 'flex', flexDirection: 'column-reverse', borderRadius: 2, overflow: 'hidden', cursor: 'pointer' }}
                    title={`${String(d.date || '')} ${fmtFull$(total)}`}
                  >
                    {series.map(s => {
                      const cost = Number((d.bySeries && d.bySeries[s.key]) || 0);
                      if (cost <= 0) return null;
                      const pct = total > 0 ? (cost / total) * 100 : 0;
                      return <div key={s.key} style={{ height: `${pct}%`, background: colorByKey[s.key] }} />;
                    })}
                  </div>
                </div>
              );
            })}
          </div>

          <div style={{ marginTop: 8, display: 'flex', fontSize: 11, color: 'var(--text-muted)' }}>
            {days.map((d, idx) => {
              const show = idx % tickStep === 0 || idx === days.length - 1;
              return (
                <div key={`tick-${String(d.date || 'na')}-${idx}`} style={{ flex: 1, minWidth: cellWidth, textAlign: 'center', whiteSpace: 'nowrap' }}>
                  {show ? shortDate(d.date) : ''}
                </div>
              );
            })}
          </div>
        </div>
      </div>

      {hoverDay && (
        <div style={{
          position: 'absolute', left: tooltipX, top: tooltipY, width: 360, maxHeight: 300, overflow: 'auto',
          background: 'var(--card-bg)', border: '1px solid var(--border)', borderRadius: 12,
          boxShadow: 'var(--shadow-lg)', padding: 12, zIndex: 3, pointerEvents: 'none'
        }}>
          <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 8 }}>
            {hoverDay.date}
          </div>
          {hoverDetails.map(row => (
            <div key={row.key} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, marginBottom: 6 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                <span style={{ width: 10, height: 10, borderRadius: 2, background: colorByKey[row.key] }} />
                <span style={{ fontSize: 12, color: 'var(--text-secondary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{row.label}</span>
              </div>
              <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-primary)', fontFamily: 'JetBrains Mono, monospace', flexShrink: 0 }}>{fmtFull$(row.cost)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function TrendChart({ data, stacked, loading, stackedLoading, onDrillDown, largeText = false }) {
  const t = useT();
  const [trendPage, setTrendPage] = React.useState('line');

  return (
    <Card>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
        <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)' }}>{t('trend_title')}</div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <div style={{ display: 'inline-flex', border: '1px solid var(--border)', borderRadius: 8, overflow: 'hidden' }}>
            {[{ key: 'line', label: t('trend_page_line') }, { key: 'stacked', label: t('trend_page_stacked') }].map(x => (
              <button key={x.key} onClick={() => setTrendPage(x.key)} style={{
                border: 'none',
                borderRight: x.key === 'line' ? '1px solid var(--border)' : 'none',
                background: trendPage === x.key ? 'var(--accent-subtle)' : 'var(--card-bg)',
                color: trendPage === x.key ? 'var(--accent)' : 'var(--text-secondary)',
                fontSize: 12, fontWeight: 600, padding: '6px 10px', cursor: 'pointer'
              }}>{x.label}</button>
            ))}
          </div>
          <button onClick={onDrillDown} style={{ background: 'none', border: '1px solid var(--border)',
            borderRadius: 7, color: 'var(--accent)', cursor: 'pointer', fontSize: 11, fontWeight: 600,
            padding: '5px 10px' }}>
            {t('view_breakdown')}
          </button>
        </div>
      </div>

      <ChartErrorBoundary>
        {trendPage === 'line'
          ? <TrendLinePage data={data} loading={loading} largeText={largeText} />
          : <TrendStackedPage stacked={stacked} loading={stackedLoading} />
        }
      </ChartErrorBoundary>
    </Card>
  );
}

function CapabilityCard({ data, loading }) {
  const t = useT();
  const COLORS = ['var(--accent)', '#10B981', '#F59E0B', '#8B5CF6', '#EC4899'];
  if (loading) return (
    <Card>
      <Skeleton width="50%" height={14} />
      <div style={{ display: 'flex', justifyContent: 'center', marginTop: 20 }}>
        <Skeleton width={140} height={140} radius="50%" />
      </div>
    </Card>
  );
  if (!data || !data.length) return (
    <Card>
      <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 12 }}>
        {t('cap_breakdown')}
      </div>
      <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />
    </Card>
  );
  return (
    <Card>
      <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 12 }}>
        {t('cap_breakdown')}
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
        <DonutChart data={data} colors={COLORS} size={140} />
        <div style={{ flex: 1, minWidth: 160 }}>
          {data.map((d, i) => (
            <div key={d.name} style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 7 }}>
              <div style={{ width: 8, height: 8, borderRadius: 2, background: COLORS[i % COLORS.length], minWidth: 8 }} />
              <div style={{ flex: 1, fontSize: 12, color: 'var(--text-secondary)' }}>{d.name}</div>
              <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-primary)' }}>
                {d.pct.toFixed(1)}%
              </div>
              <div style={{ fontSize: 11, color: 'var(--text-muted)', width: 72, textAlign: 'right',
                fontFamily: 'JetBrains Mono, monospace' }}>
                {fmtFull$(d.cost)}
              </div>
            </div>
          ))}
        </div>
      </div>
    </Card>
  );
}

function ModelCard({ data, loading }) {
  const t = useT();
  const COLORS = ['var(--accent)', '#6366F1', '#8B5CF6', '#10B981', '#F59E0B', '#EC4899'];
  if (loading) return (
    <Card>
      <Skeleton width="40%" height={14} />
      <div style={{ marginTop: 16 }}>
        {[1,2,3,4].map(i => <div key={i} style={{ marginBottom: 10 }}><Skeleton width="100%" height={10} /></div>)}
      </div>
    </Card>
  );
  if (!data || !data.length) return (
    <Card>
      <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 14 }}>{t('cost_by_model')}</div>
      <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />
    </Card>
  );
  const max = Math.max(...data.map(d => d.cost));
  return (
    <Card>
      <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 14 }}>
        {t('cost_by_model')}
      </div>
      {data.map((d, i) => (
        <HorizontalBar key={d.model} label={d.model}
          value={`$${d.cost.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`} pct={(d.cost / max) * 100}
          color={COLORS[i % COLORS.length]} />
      ))}
    </Card>
  );
}

function OverviewPage({ onDrillDown, dateRange, orgId, projectId, source, largeText = false }) {
  const t = useT();
  // dateRange IS the period enum ('today' | '7d' | '30d' | 'MTD' | '__custom__|...').
  // No i18n reverse-lookup (architecture principle ③).
  const period = React.useMemo(
    () => (typeof dateRange === 'string' && dateRange.startsWith('__custom__|')) ? 'custom' : (dateRange || 'MTD'),
    [dateRange]);

  const { loading: statsLoading, data: stats, error: statsErr, reload: reloadStats } =
    useAsync(() => window.API.getOverview(dateRange, orgId, projectId, source), [dateRange, orgId, projectId, source]);

  // getTrend / getCostTrendStacked both accept the period enum; backend resolves the window.
  const { loading: trendLoading, data: trendRaw, reload: reloadTrend } =
    useAsync(() => window.API.getTrend(orgId, projectId, dateRange, source), [orgId, projectId, source, dateRange]);
  const { loading: stackedLoading, data: stackedRaw } =
    useAsync(() => {
      const fn = window?.API?.getCostTrendStacked;
      if (typeof fn !== 'function') return Promise.resolve(null);
      return fn(dateRange, orgId, projectId, source, 10);
    }, [dateRange, orgId, projectId, source]);

  const { loading: capLoading, data: capRaw } =
    useAsync(() => window.API.getCostBreakdown('capability', dateRange, orgId, projectId, source), [dateRange, orgId, projectId, source]);

  const { loading: modelLoading, data: modelRaw } =
    useAsync(() => window.API.getCostBreakdown('model', dateRange, orgId, projectId, source), [dateRange, orgId, projectId, source]);

  const trend     = trendRaw  || [];
  const stacked   = stackedRaw || null;
  // getCostBreakdown returns { items, totalCostUsd, ... } — read .items, not the wrapper.
  const capData   = ((capRaw?.items)   || []).map(x => ({ name: x.key, cost: x.cost, pct: x.pct }));
  const modelData = ((modelRaw?.items) || []).map(x => ({ model: x.key, cost: x.cost }));
  const sparkVals = trend.map(d => d.cost);
  const costLabel = React.useMemo(() => {
    if (period === 'today')  return t('cost_today');
    if (period === '7d')     return t('cost_7d');
    if (period === '30d')    return t('cost_30d');
    if (period === 'custom') return t('cost_range');
    return t('monthly_cost');
  }, [period, t]);
  const deltaSub = React.useMemo(() => {
    if (period === 'today') return t('vs_yesterday');
    if (period === 'MTD')   return t('vs_last_month');
    if (period === '7d')    return t('vs_prev_7d');
    if (period === '30d')   return t('vs_prev_30d');
    return t('vs_prev_period');
  }, [period, t]);

  const s = stats || {};
  const statsLoaded = !!stats;

  // Show '—' only when Azure is the source, cost exists, but metrics haven't synced yet (24-48h delay)
  const azureMetricsDelayed =
    statsLoaded &&
    source !== 'openai' &&
    (s.monthlyCost || 0) > 0 &&
    !(s.totalRequests || 0);
  const metricVal = (v) => !statsLoaded ? '—' : azureMetricsDelayed ? '—' : fmtN(v || 0);

  if (statsErr) return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)' }}>
      <ErrorState onRetry={reloadStats} />
    </div>
  );

  return (
    <div style={{ padding: 'clamp(16px, 2.5vw, 24px) clamp(16px, 3vw, 28px)', maxWidth: 1400 }}>
      <div style={{ marginBottom: 20 }}>
        <h1 style={{ fontSize: 20, fontWeight: 700, color: 'var(--text-primary)', margin: 0, letterSpacing: '-0.3px' }}>
          {t('nav_overview')}
        </h1>
      </div>

      {/* KPI Row */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(185px, 1fr))', gap: 14, marginBottom: 16 }}>
        <StatCard loading={statsLoading} label={costLabel} value={statsLoaded ? fmt$(s.monthlyCost || 0) : '—'}
          delta={s.monthlyCostDelta} sub={deltaSub} color="var(--accent)"
          sparkData={sparkVals.slice(-14)} />
        <StatCard loading={statsLoading} label={t('total_requests')} value={metricVal(s.totalRequests)}
          delta={azureMetricsDelayed ? undefined : s.totalRequestsDelta} sub={deltaSub} color="#6366F1"
          sparkData={trend.slice(-14).map(d => d.requests)} />
        <StatCard loading={statsLoading} label={t('input_tokens')} value={metricVal(s.inputTokens)}
          color="#10B981"
          sparkData={trend.slice(-14).map(d => d.inputTokens)} />
        <StatCard loading={statsLoading} label={t('output_tokens')} value={metricVal(s.outputTokens)}
          color="#F59E0B"
          sparkData={trend.slice(-14).map(d => d.outputTokens)} />
        <StatCard loading={statsLoading} label={t('avg_daily_cost')} value={statsLoaded ? fmt$(s.avgDailyCost || 0) : '—'}
          color="#8B5CF6" />
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '8px 12px', marginBottom: 12,
        background: 'rgba(245,158,11,0.08)', border: '1px solid rgba(245,158,11,0.25)', borderRadius: 8,
        fontSize: 12, color: '#B45309' }}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M12 9v2m0 4h.01M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/>
        </svg>
        {t('azure_metrics_delay')}
      </div>

      {/* Trend — full width */}
      <div style={{ marginBottom: 16 }}>
        <TrendChart
          data={trend}
          stacked={stacked}
          loading={trendLoading}
          stackedLoading={stackedLoading}
          onDrillDown={onDrillDown}
          largeText={largeText}
        />
      </div>

      {/* Bottom row — gracefully wraps when viewport too narrow for 1:2 layout */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: 14 }}>
        <ModelCard data={modelData} loading={modelLoading} />
        <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
          <Card>
            <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-primary)', marginBottom: 14 }}>
              {t('daily_requests')}
            </div>
            {trendLoading
              ? <Skeleton width="100%" height={largeText ? 270 : 220} radius={8} />
              : trend.length > 0
                ? <BarChart data={trend.slice(-14)} xKey="date" yKey="requests" color="#6366F1"
                    formatY={v => fmtN(v)} largeText={largeText} />
                : <EmptyState title={t('no_data_title')} description={t('no_data_desc')} />
            }
          </Card>
          <CapabilityCard data={capData} loading={capLoading} />
        </div>
      </div>
    </div>
  );
}

Object.assign(window, { OverviewPage });
