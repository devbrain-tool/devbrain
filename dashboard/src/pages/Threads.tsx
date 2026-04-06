import { useEffect, useState } from 'react';
import { api, type DevBrainThread, type ThreadState, type Observation } from '../api/client';

const STATE_COLORS: Record<ThreadState, string> = {
  Active: '#22c55e',
  Paused: '#eab308',
  Closed: '#6b7280',
  Archived: '#6b7280',
};

const ALL_STATES: ThreadState[] = ['Active', 'Paused', 'Closed', 'Archived'];

export default function Threads() {
  const [threads, setThreads] = useState<DevBrainThread[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [stateFilter, setStateFilter] = useState<ThreadState | 'All'>('All');
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [observations, setObservations] = useState<Observation[]>([]);
  const [loadingObs, setLoadingObs] = useState(false);

  useEffect(() => {
    api
      .threads()
      .then((data) => {
        setThreads(data);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  }, []);

  const handleExpand = (id: string) => {
    if (expandedId === id) {
      setExpandedId(null);
      setObservations([]);
      return;
    }
    setExpandedId(id);
    setLoadingObs(true);
    api
      .thread(id)
      .then((data) => {
        setObservations(data.observations || []);
        setLoadingObs(false);
      })
      .catch(() => {
        setObservations([]);
        setLoadingObs(false);
      });
  };

  if (error) return <div style={styles.error}>Error: {error}</div>;
  if (loading) return <div style={styles.loading}>Loading threads...</div>;

  const filtered =
    stateFilter === 'All'
      ? threads
      : threads.filter((t) => t.state === stateFilter);

  return (
    <div style={styles.container}>
      <h1>Threads</h1>

      <div style={styles.filterRow}>
        {(['All', ...ALL_STATES] as const).map((s) => (
          <button
            key={s}
            onClick={() => setStateFilter(s)}
            style={{
              ...styles.filterBtn,
              background: stateFilter === s ? '#2a2a4a' : 'transparent',
              borderColor: stateFilter === s ? '#6366f1' : '#2e303a',
            }}
          >
            {s}
          </button>
        ))}
        <span style={styles.count}>
          {filtered.length} thread{filtered.length !== 1 ? 's' : ''}
        </span>
      </div>

      {filtered.length === 0 && (
        <p style={styles.empty}>No threads found.</p>
      )}

      <div style={styles.list}>
        {filtered.map((t) => (
          <div key={t.id}>
            <div
              style={styles.card}
              onClick={() => handleExpand(t.id)}
              role="button"
              tabIndex={0}
            >
              <div style={styles.cardHeader}>
                <span
                  style={{
                    ...styles.stateBadge,
                    background: STATE_COLORS[t.state] + '22',
                    color: STATE_COLORS[t.state],
                    borderColor: STATE_COLORS[t.state] + '44',
                  }}
                >
                  {t.state}
                </span>
                <span style={styles.title}>
                  {t.title || `Thread ${t.id.slice(0, 8)}`}
                </span>
                <span style={styles.project}>{t.project}</span>
              </div>
              <div style={styles.meta}>
                <span>{t.observationCount} observations</span>
                <span>Started {new Date(t.startedAt).toLocaleDateString()}</span>
                <span>
                  Last activity {new Date(t.lastActivity).toLocaleString()}
                </span>
              </div>
              {t.summary && (
                <div style={styles.summary}>{t.summary}</div>
              )}
            </div>

            {expandedId === t.id && (
              <div style={styles.obsPanel}>
                {loadingObs ? (
                  <p style={styles.obsLoading}>Loading observations...</p>
                ) : observations.length === 0 ? (
                  <p style={styles.obsLoading}>No observations in this thread.</p>
                ) : (
                  observations.map((obs) => (
                    <div key={obs.id} style={styles.obsCard}>
                      <div style={styles.obsHeader}>
                        <span style={styles.obsType}>{obs.eventType}</span>
                        <span style={styles.obsTime}>
                          {new Date(obs.timestamp).toLocaleString()}
                        </span>
                      </div>
                      <div style={styles.obsContent}>
                        {obs.summary || obs.rawContent.slice(0, 200)}
                        {!obs.summary && obs.rawContent.length > 200 ? '...' : ''}
                      </div>
                    </div>
                  ))
                )}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 800, margin: '0 auto' },
  filterRow: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
    marginBottom: '1.5rem',
    flexWrap: 'wrap' as const,
  },
  filterBtn: {
    padding: '0.35rem 0.75rem',
    border: '1px solid #2e303a',
    borderRadius: 4,
    color: '#e0e0ff',
    fontSize: '0.85rem',
    cursor: 'pointer',
    background: 'transparent',
  },
  count: {
    fontSize: '0.85rem',
    color: '#9ca3af',
    marginLeft: 'auto',
  },
  list: { display: 'flex', flexDirection: 'column', gap: '0.5rem' },
  card: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '1rem',
    border: '1px solid #2e303a',
    cursor: 'pointer',
  },
  cardHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
    marginBottom: '0.5rem',
    flexWrap: 'wrap' as const,
  },
  stateBadge: {
    fontSize: '0.75rem',
    padding: '2px 8px',
    borderRadius: 4,
    border: '1px solid',
    fontWeight: 600,
    textTransform: 'uppercase' as const,
  },
  title: {
    fontSize: '0.95rem',
    color: '#f3f4f6',
    fontWeight: 600,
  },
  project: {
    fontSize: '0.8rem',
    color: '#60a5fa',
    background: '#1e293b',
    padding: '2px 8px',
    borderRadius: 4,
    fontFamily: 'monospace',
    marginLeft: 'auto',
  },
  meta: {
    display: 'flex',
    gap: '1.5rem',
    fontSize: '0.8rem',
    color: '#6b7280',
  },
  summary: {
    marginTop: '0.5rem',
    fontSize: '0.85rem',
    color: '#d1d5db',
    lineHeight: 1.4,
  },
  obsPanel: {
    marginLeft: '1rem',
    borderLeft: '2px solid #2e303a',
    paddingLeft: '1rem',
    marginBottom: '0.5rem',
  },
  obsLoading: {
    color: '#9ca3af',
    fontSize: '0.85rem',
    padding: '0.5rem 0',
  },
  obsCard: {
    background: '#181820',
    borderRadius: 6,
    padding: '0.75rem',
    marginBottom: '0.5rem',
    border: '1px solid #23242e',
  },
  obsHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    marginBottom: '0.35rem',
  },
  obsType: {
    fontSize: '0.8rem',
    color: '#c084fc',
    fontWeight: 600,
    textTransform: 'uppercase' as const,
  },
  obsTime: { fontSize: '0.75rem', color: '#6b7280' },
  obsContent: { fontSize: '0.85rem', color: '#d1d5db', lineHeight: 1.4 },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
  empty: { color: '#6b7280', textAlign: 'center' as const, padding: '2rem' },
};
