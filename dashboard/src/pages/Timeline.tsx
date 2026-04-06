import { useEffect, useState } from 'react';
import { api, type Observation } from '../api/client';

const EVENT_ICONS: Record<string, string> = {
  commit: '\u{1F4DD}',
  build: '\u{1F527}',
  test: '\u{1F9EA}',
  error: '\u{1F6A8}',
  debug: '\u{1F41E}',
  deploy: '\u{1F680}',
  review: '\u{1F50D}',
};

export default function Timeline() {
  const [observations, setObservations] = useState<Observation[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [offset, setOffset] = useState(0);
  const limit = 20;

  const load = (currentOffset: number, append: boolean) => {
    setLoading(true);
    api
      .observations({ limit, offset: currentOffset })
      .then((data) => {
        setObservations((prev) => (append ? [...prev, ...data] : data));
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  };

  useEffect(() => {
    load(0, false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleLoadMore = () => {
    const next = offset + limit;
    setOffset(next);
    load(next, true);
  };

  if (error) return <div style={styles.error}>Error: {error}</div>;

  return (
    <div style={styles.container}>
      <h1>Timeline</h1>
      {observations.length === 0 && !loading && (
        <p style={styles.empty}>No observations yet.</p>
      )}
      <div style={styles.list}>
        {observations.map((obs) => (
          <div key={obs.id} style={styles.card}>
            <div style={styles.cardHeader}>
              <span style={styles.icon}>
                {EVENT_ICONS[obs.eventType.toLowerCase()] || '\u{1F4CB}'}
              </span>
              <span style={styles.eventType}>{obs.eventType}</span>
              <span style={styles.project}>{obs.project}</span>
              <span style={styles.time}>
                {new Date(obs.timestamp).toLocaleString()}
              </span>
            </div>
            <div style={styles.content}>
              {obs.summary || obs.rawContent.slice(0, 200)}
              {!obs.summary && obs.rawContent.length > 200 ? '...' : ''}
            </div>
            {obs.tags.length > 0 && (
              <div style={styles.tags}>
                {obs.tags.map((t) => (
                  <span key={t} style={styles.tag}>{t}</span>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>
      {loading && <p style={styles.loading}>Loading...</p>}
      {!loading && observations.length > 0 && (
        <button onClick={handleLoadMore} style={styles.loadMore}>
          Load more
        </button>
      )}
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 800, margin: '0 auto' },
  list: { display: 'flex', flexDirection: 'column', gap: '0.75rem' },
  card: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '1rem',
    border: '1px solid #2e303a',
  },
  cardHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
    marginBottom: '0.5rem',
    flexWrap: 'wrap' as const,
  },
  icon: { fontSize: '1.1rem' },
  eventType: {
    fontWeight: 600,
    color: '#c084fc',
    fontSize: '0.85rem',
    textTransform: 'uppercase' as const,
  },
  project: {
    fontSize: '0.8rem',
    color: '#60a5fa',
    background: '#1e293b',
    padding: '2px 8px',
    borderRadius: 4,
  },
  time: { fontSize: '0.8rem', color: '#6b7280', marginLeft: 'auto' },
  content: { color: '#d1d5db', fontSize: '0.9rem', lineHeight: 1.5 },
  tags: { display: 'flex', gap: '0.35rem', marginTop: '0.5rem', flexWrap: 'wrap' as const },
  tag: {
    fontSize: '0.75rem',
    background: '#2a2a4a',
    color: '#a5b4fc',
    padding: '2px 6px',
    borderRadius: 3,
  },
  loading: { textAlign: 'center' as const, color: '#9ca3af', padding: '1rem' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
  empty: { color: '#6b7280', textAlign: 'center' as const, padding: '2rem' },
  loadMore: {
    display: 'block',
    margin: '1.5rem auto',
    padding: '0.5rem 1.5rem',
    background: '#2a2a4a',
    color: '#e0e0ff',
    border: '1px solid #3b3b6b',
    borderRadius: 6,
    cursor: 'pointer',
    fontSize: '0.9rem',
  },
};
