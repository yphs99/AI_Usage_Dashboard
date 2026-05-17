// charts.jsx — SVG chart components for API Usage Dashboard

// ── Utility ───────────────────────────────────────────────────────────────────
function clamp(v, lo, hi) { return Math.max(lo, Math.min(hi, v)); }

function pathD(pts) {
  if (!pts.length) return '';
  return pts.map((p, i) => `${i === 0 ? 'M' : 'L'}${p[0]},${p[1]}`).join(' ');
}

function smoothPath(pts) {
  if (pts.length < 2) return pathD(pts);
  let d = `M${pts[0][0]},${pts[0][1]}`;
  for (let i = 1; i < pts.length; i++) {
    const prev = pts[i - 1], curr = pts[i];
    const cpx = (prev[0] + curr[0]) / 2;
    d += ` C${cpx},${prev[1]} ${cpx},${curr[1]} ${curr[0]},${curr[1]}`;
  }
  return d;
}

// ── LineChart ─────────────────────────────────────────────────────────────────
function LineChart({ data, xKey, yKey, color = '#3B82F6', height = 200, showDots = false,
  showGrid = true, showYLabels = true, formatX, formatY, onHover, gradientId, width = 500, largeText = false, minY }) {
  const [hoverIdx, setHoverIdx] = React.useState(null);
  const fontScale = largeText ? 1.28 : 1;
  const W = Math.max(320, Number(width) || 500);
  const H = Math.max(140, Number(height) || 200);
  const PAD = { t: 16, r: 12, b: largeText ? 46 : 40, l: largeText ? 72 : 64 };
  const iW = W - PAD.l - PAD.r;
  const iH = H - PAD.t - PAD.b;

  const vals = data.map(d => d[yKey]);
  const minV = Math.min(...vals);
  const maxV = Math.max(...vals);
  const range = maxV - minV || 1;
  const pad = range * 0.1;
  const lo = Math.max(minY ?? -Infinity, minV - pad), hi = maxV + pad;

  const toX = (i) => PAD.l + (i / (data.length - 1)) * iW;
  const toY = (v) => PAD.t + (1 - (v - lo) / (hi - lo)) * iH;

  const pts = data.map((d, i) => [toX(i), toY(d[yKey])]);
  const uid = gradientId || 'lc';

  const yTicks = 4;
  const gridYs = Array.from({ length: yTicks + 1 }, (_, i) =>
    lo + (i / yTicks) * (hi - lo));

  const xStep = Math.max(1, Math.floor(data.length / 6));
  const xLabels = data.map((d, i) => ({ i, label: formatX ? formatX(d[xKey], i) : d[xKey] }))
    .filter((_, i) => i % xStep === 0 || i === data.length - 1);

  return (
    <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" width="100%" height={height} style={{ overflow: 'visible', display: 'block' }}>
      <defs>
        <linearGradient id={uid} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.18" />
          <stop offset="100%" stopColor={color} stopOpacity="0.01" />
        </linearGradient>
        <clipPath id={`clip-${uid}`}>
          <rect x={PAD.l} y={PAD.t} width={iW} height={iH} />
        </clipPath>
      </defs>

      {showGrid && gridYs.map((v, i) => (
        <g key={i}>
          <line x1={PAD.l} y1={toY(v)} x2={PAD.l + iW} y2={toY(v)}
            stroke="currentColor" strokeOpacity="0.1" strokeWidth="0.6" />
          {showYLabels && (
            <text x={PAD.l - 4} y={toY(v)} textAnchor="end" dominantBaseline="middle"
              fontSize={10 * fontScale} fill="currentColor" fillOpacity="0.6">
              {formatY ? formatY(v) : v.toFixed(0)}
            </text>
          )}
        </g>
      ))}

      {xLabels.map(({ i, label }) => (
        <text key={i} x={toX(i)} y={H - PAD.b + 12} textAnchor="middle"
          fontSize={10 * fontScale} fill="currentColor" fillOpacity="0.65">{label}</text>
      ))}

      <path d={pathD(pts) + ` L${pts[pts.length-1][0]},${PAD.t+iH} L${pts[0][0]},${PAD.t+iH} Z`}
        fill={`url(#${uid})`} clipPath={`url(#clip-${uid})`} />
      <path d={pathD(pts)} fill="none" stroke={color} strokeWidth="2.2"
        clipPath={`url(#clip-${uid})`} />

      {showDots && pts.map(([x, y], i) => (
        <circle key={i} cx={x} cy={y} r={hoverIdx === i ? 5 : 3.5}
          fill={color} stroke="white" strokeWidth="1.2" />
      ))}

      {hoverIdx !== null && (
        <line x1={pts[hoverIdx][0]} y1={PAD.t} x2={pts[hoverIdx][0]} y2={PAD.t + iH}
          stroke={color} strokeWidth="0.7" strokeDasharray="2 1" strokeOpacity="0.65" />
      )}

      <rect x={PAD.l} y={PAD.t} width={iW} height={iH} fill="transparent"
        onMouseMove={(e) => {
          const rect = e.currentTarget.getBoundingClientRect();
          const x = (e.clientX - rect.left) / rect.width * iW;
          const idx = clamp(Math.round(x / iW * (data.length - 1)), 0, data.length - 1);
          setHoverIdx(idx);
          onHover && onHover(data[idx], idx);
        }}
        onMouseLeave={() => { setHoverIdx(null); onHover && onHover(null, null); }}
      />

      {hoverIdx !== null && (() => {
        const [hx, hy] = pts[hoverIdx];
        const d = data[hoverIdx];
        const label = formatY ? formatY(d[yKey]) : d[yKey].toFixed(2);
        const tx = hx > W * 0.7 ? hx - 3 : hx + 3;
        const anchor = hx > W * 0.7 ? 'end' : 'start';
        return (
          <g>
            <circle cx={hx} cy={hy} r="5" fill={color} stroke="white" strokeWidth="1.4" />
            <rect x={tx - (anchor === 'end' ? 60 * fontScale : 0)} y={hy - (16 * fontScale)} width={60 * fontScale} height={14 * fontScale}
              rx="1.3" fill={color} fillOpacity="0.92" />
            <text x={tx + (anchor === 'end' ? -30 * fontScale : 30 * fontScale)} y={hy - (7 * fontScale)} textAnchor="middle"
              fontSize={9 * fontScale} fill="white" fontWeight="700">{label}</text>
          </g>
        );
      })()}
    </svg>
  );
}

// ── BarChart ──────────────────────────────────────────────────────────────────
function BarChart({ data, xKey, yKey, color = '#3B82F6', height = 160, formatY, onBarClick, largeText = false }) {
  const [hoverIdx, setHoverIdx] = React.useState(null);
  const fontScale = largeText ? 1.28 : 1;
  const W = 500, H = 200;
  const PAD = { t: 22, r: 10, b: largeText ? 50 : 44, l: 48 };
  const iW = W - PAD.l - PAD.r;
  const iH = H - PAD.t - PAD.b;

  const vals = data.map(d => d[yKey]);
  const maxV = Math.max(...vals) * 1.1 || 1;
  const barW = iW / data.length * 0.7;
  const gap  = iW / data.length;

  const yTicks = 4;
  const gridYs = Array.from({ length: yTicks + 1 }, (_, i) => (i / yTicks) * maxV);

  return (
    <svg viewBox={`0 0 ${W} ${H}`} width="100%" style={{ overflow: 'visible', display: 'block' }}>
      {gridYs.map((v, i) => {
        const gy = PAD.t + (1 - v/maxV)*iH;
        return (
          <g key={i}>
            <line x1={PAD.l} y1={gy} x2={PAD.l+iW} y2={gy}
              stroke="currentColor" strokeOpacity="0.1" strokeWidth="0.7" />
            {v > 0 && (
              <text x={PAD.l - 4} y={gy} textAnchor="end" dominantBaseline="middle"
                fontSize={7 * fontScale} fill="currentColor" fillOpacity="0.5">
                {formatY ? formatY(v) : v.toFixed(0)}
              </text>
            )}
          </g>
        );
      })}

      {data.map((d, i) => {
        const x = PAD.l + i * gap + gap / 2 - barW / 2;
        const bH = Math.max(1, (d[yKey] / maxV) * iH);
        const y = PAD.t + iH - bH;
        const isHov = hoverIdx === i;
        return (
          <g key={i} style={{ cursor: onBarClick ? 'pointer' : 'default' }}
            onMouseEnter={() => setHoverIdx(i)} onMouseLeave={() => setHoverIdx(null)}
            onClick={() => onBarClick && onBarClick(d, i)}>
            <rect x={x} y={y} width={barW} height={bH} rx="3"
              fill={color} fillOpacity={isHov ? 1 : 0.75} />
            {isHov && (
              <text x={x + barW/2} y={y - 4} textAnchor="middle" fontSize={7 * fontScale}
                fill={color} fontWeight="700">{formatY ? formatY(d[yKey]) : d[yKey]}</text>
            )}
          </g>
        );
      })}

      {data.map((d, i) => (
        <text key={i} x={PAD.l + i * gap + gap / 2} y={H - PAD.b + 10}
          textAnchor="middle" fontSize={7 * fontScale} fill="currentColor" fillOpacity="0.65">
          {String(d[xKey]).slice(5)}
        </text>
      ))}
    </svg>
  );
}

// ── DonutChart ────────────────────────────────────────────────────────────────
function DonutChart({ data, colors, size = 160 }) {
  const [hoverIdx, setHoverIdx] = React.useState(null);
  const cx = 50, cy = 50, r = 32, inner = 20;
  const total = data.reduce((s, d) => s + d.pct, 0);
  let angle = -Math.PI / 2;

  function arc(pct, hov) {
    const a = (pct / total) * Math.PI * 2 * (hov ? 1.02 : 1);
    const x1 = cx + r * Math.cos(angle);
    const y1 = cy + r * Math.sin(angle);
    const x2 = cx + r * Math.cos(angle + a);
    const y2 = cy + r * Math.sin(angle + a);
    const xi1 = cx + inner * Math.cos(angle);
    const yi1 = cy + inner * Math.sin(angle);
    const xi2 = cx + inner * Math.cos(angle + a);
    const yi2 = cy + inner * Math.sin(angle + a);
    const large = a > Math.PI ? 1 : 0;
    return `M${xi1},${yi1} L${x1},${y1} A${r},${r} 0 ${large} 1 ${x2},${y2} L${xi2},${yi2} A${inner},${inner} 0 ${large} 0 ${xi1},${yi1} Z`;
  }

  const slices = data.map((d, i) => {
    const path = arc(d.pct, hoverIdx === i);
    angle += (d.pct / total) * Math.PI * 2;
    return { ...d, path, color: colors[i % colors.length] };
  });

  const hov = hoverIdx !== null ? data[hoverIdx] : null;

  return (
    <svg viewBox="0 0 100 100" width={size} height={size}>
      {slices.map((s, i) => (
        <path key={i} d={s.path} fill={s.color} fillOpacity={hoverIdx === null || hoverIdx === i ? 1 : 0.55}
          style={{ cursor: 'pointer', transition: 'fill-opacity 0.15s' }}
          onMouseEnter={() => setHoverIdx(i)} onMouseLeave={() => setHoverIdx(null)} />
      ))}
      {hov ? (
        <>
          <text x={cx} y={cy - 3} textAnchor="middle" fontSize="7" fontWeight="700" fill="currentColor">{hov.pct.toFixed(1)}%</text>
          <text x={cx} y={cy + 5} textAnchor="middle" fontSize="3.8" fill="currentColor" fillOpacity="0.6">{hov.name}</text>
        </>
      ) : (
        <text x={cx} y={cy + 2} textAnchor="middle" fontSize="5" fill="currentColor" fillOpacity="0.4">Hover</text>
      )}
    </svg>
  );
}

// ── SparkLine ─────────────────────────────────────────────────────────────────
function SparkLine({ values, color = '#3B82F6', width = '100%', height = 40 }) {
  const min = Math.min(...values), max = Math.max(...values);
  const range = max - min || 1;
  // Wide viewBox keeps aspect ratio close to rendered output → no distortion
  const W = 300, H = 60;
  const pad = { t: 6, b: 6, l: 1, r: 1 };
  const iW = W - pad.l - pad.r, iH = H - pad.t - pad.b;

  const pts = values.map((v, i) => [
    pad.l + (i / (values.length - 1)) * iW,
    pad.t + (1 - (v - min) / range) * iH
  ]);

  const fillPath = pathD(pts)
    + ` L${pts[pts.length - 1][0]},${H - pad.b} L${pts[0][0]},${H - pad.b} Z`;

  const uid = `spark-${color.replace(/[^a-z0-9]/gi, '')}`;

  return (
    <svg viewBox={`0 0 ${W} ${H}`} width={width} height={height}
      preserveAspectRatio="none" style={{ display: 'block' }}>
      <defs>
        <linearGradient id={uid} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.18" />
          <stop offset="100%" stopColor={color} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={fillPath} fill={`url(#${uid})`} />
      <path d={pathD(pts)} fill="none" stroke={color} strokeWidth="3"
        strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

// ── StackedBar ────────────────────────────────────────────────────────────────
function HorizontalBar({ label, value, maxValue, color, pct }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
      <div style={{ width: '120px', fontSize: '12px', color: 'var(--text-muted)', whiteSpace: 'nowrap',
        overflow: 'hidden', textOverflow: 'ellipsis' }}>{label}</div>
      <div style={{ flex: 1, height: '6px', background: 'var(--border)', borderRadius: '3px', overflow: 'hidden' }}>
        <div style={{ width: `${pct}%`, height: '100%', background: color, borderRadius: '3px',
          transition: 'width 0.6s ease' }} />
      </div>
      <div style={{ width: '80px', textAlign: 'right', fontSize: '12px', fontWeight: '600',
        color: 'var(--text-primary)', fontFamily: 'JetBrains Mono, monospace', letterSpacing: '-0.3px' }}>{value}</div>
    </div>
  );
}

Object.assign(window, { LineChart, BarChart, DonutChart, SparkLine, HorizontalBar });
