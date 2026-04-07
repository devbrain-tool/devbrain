import { useEffect, useState } from 'react';
import { api, type GrowthReport, type GrowthMilestone } from '../api/client';

const milestoneColors: Record<string, string> = {
  First: '#3b82f6',
  Streak: '#eab308',
  Improvement: '#22c55e',
};

export default function Growth() {
  const [report, setReport] = useState<GrowthReport | null>(null);
  const [milestones, setMilestones] = useState<GrowthMilestone[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      api.growth().catch(() => null),
      api.growthMilestones().catch(() => []),
    ])
      .then(([reportData, milestonesData]) => {
        if (reportData && 'id' in reportData) setReport(reportData);
        setMilestones(milestonesData as GrowthMilestone[]);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  }, []);

  if (error) return <div style={styles.error}>Error: {error}</div>;
  if (loading) return <div style={styles.loading}>Loading growth data...</div>;

  return (
    <div style={styles.container}>
      <h1>Developer Growth</h1>

      {/* Narrative */}
      {report?.narrative && (
        <div style={styles.narrativeCard}>
          <div style={styles.narrativeLabel}>This Week</div>
          <div style={styles.narrative}>{report.narrative}</div>
        </div>
      )}

      {/* Metrics */}
      {report?.metrics && report.metrics.length > 0 && (
        <div style={styles.metricsGrid}>
          {report.metrics.map((m) => (
            <div key={m.dimension} style={styles.metricCard}>
              <div style={styles.metricDimension}>{m.dimension.replace(/_/g, ' ')}</div>
              <div style={styles.metricValue}>{m.value.toFixed(2)}</div>
            </div>
          ))}
        </div>
      )}

      {!report && (
        <p style={styles.empty}>
          No growth reports yet. The growth agent runs weekly (Monday 8 AM).
        </p>
      )}

      {/* Milestones */}
      <h2 style={styles.sectionTitle}>Milestones</h2>
      {milestones.length === 0 ? (
        <p style={styles.empty}>No milestones yet.</p>
      ) : (
        <div style={styles.milestoneList}>
          {milestones.map((m) => (
            <div key={m.id} style={styles.milestoneCard}>
              <span
                style={{
                  ...styles.milestoneBadge,
                  background: milestoneColors[m.type] || '#6b7280',
                }}
              >
                {m.type}
              </span>
              <span style={styles.milestoneDesc}>{m.description}</span>
              <span style={styles.milestoneDate}>
                {new Date(m.achievedAt).toLocaleDateString()}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 800, margin: '0 auto' },
  narrativeCard: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '1.25rem',
    border: '1px solid #22c55e33',
    marginBottom: '1.5rem',
  },
  narrativeLabel: {
    fontSize: '0.75rem',
    color: '#22c55e',
    fontWeight: 700,
    textTransform: 'uppercase' as const,
    marginBottom: '0.5rem',
  },
  narrative: { color: '#e5e7eb', fontSize: '0.95rem', lineHeight: 1.5 },
  metricsGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))',
    gap: '0.75rem',
    marginBottom: '2rem',
  },
  metricCard: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '1rem',
    border: '1px solid #2e303a',
    textAlign: 'center' as const,
  },
  metricDimension: {
    fontSize: '0.75rem',
    color: '#9ca3af',
    textTransform: 'capitalize' as const,
    marginBottom: '0.5rem',
  },
  metricValue: {
    fontSize: '1.5rem',
    fontWeight: 700,
    color: '#f3f4f6',
    fontFamily: 'monospace',
  },
  sectionTitle: { fontSize: '1.1rem', color: '#d1d5db', marginBottom: '1rem' },
  milestoneList: { display: 'flex', flexDirection: 'column', gap: '0.5rem' },
  milestoneCard: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.75rem',
    padding: '0.5rem 0.75rem',
    background: '#1f2028',
    borderRadius: 6,
    border: '1px solid #2e303a',
  },
  milestoneBadge: {
    fontSize: '0.65rem',
    color: '#fff',
    padding: '2px 8px',
    borderRadius: 4,
    fontWeight: 700,
    textTransform: 'uppercase' as const,
    flexShrink: 0,
  },
  milestoneDesc: { flex: 1, color: '#e5e7eb', fontSize: '0.85rem' },
  milestoneDate: { fontSize: '0.75rem', color: '#6b7280', flexShrink: 0 },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
  empty: { color: '#6b7280', textAlign: 'center' as const, padding: '1rem' },
};
