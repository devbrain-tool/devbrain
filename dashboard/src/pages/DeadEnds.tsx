import { useEffect, useState } from 'react';
import { api, type DeadEnd } from '../api/client';

export default function DeadEnds() {
  const [deadEnds, setDeadEnds] = useState<DeadEnd[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState('');

  useEffect(() => {
    api
      .deadEnds()
      .then((data) => {
        setDeadEnds(data);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  }, []);

  if (error) return <div style={styles.error}>Error: {error}</div>;
  if (loading) return <div style={styles.loading}>Loading dead ends...</div>;

  const filtered = filter
    ? deadEnds.filter((d) =>
        d.project.toLowerCase().includes(filter.toLowerCase())
      )
    : deadEnds;

  return (
    <div style={styles.container}>
      <h1>Dead Ends</h1>

      <div style={styles.filterRow}>
        <input
          type="text"
          placeholder="Filter by project..."
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          style={styles.input}
        />
        <span style={styles.count}>
          {filtered.length} dead end{filtered.length !== 1 ? 's' : ''}
        </span>
      </div>

      {filtered.length === 0 && (
        <p style={styles.empty}>
          {deadEnds.length === 0
            ? 'No dead ends recorded yet.'
            : 'No dead ends match the filter.'}
        </p>
      )}

      <div style={styles.list}>
        {filtered.map((de) => (
          <div key={de.id} style={styles.card}>
            <div style={styles.cardHeader}>
              <span style={styles.project}>{de.project}</span>
              <span style={styles.date}>
                {new Date(de.detectedAt).toLocaleDateString()}
              </span>
            </div>

            <div style={styles.description}>{de.description}</div>

            <div style={styles.detail}>
              <span style={styles.detailLabel}>Approach tried:</span>
              <span style={styles.detailValue}>{de.approach}</span>
            </div>

            <div style={styles.detail}>
              <span style={styles.detailLabel}>Why it failed:</span>
              <span style={styles.detailValue}>{de.reason}</span>
            </div>

            {de.filesInvolved.length > 0 && (
              <div style={styles.filesSection}>
                <span style={styles.detailLabel}>Files involved:</span>
                <div style={styles.fileList}>
                  {de.filesInvolved.map((f) => (
                    <span key={f} style={styles.fileTag}>
                      {f}
                    </span>
                  ))}
                </div>
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
    gap: '1rem',
    marginBottom: '1.5rem',
  },
  input: {
    flex: 1,
    padding: '0.5rem 0.75rem',
    background: '#1f2028',
    border: '1px solid #2e303a',
    borderRadius: 6,
    color: '#f3f4f6',
    fontSize: '0.9rem',
    fontFamily: 'monospace',
    outline: 'none',
  },
  count: {
    fontSize: '0.85rem',
    color: '#9ca3af',
    whiteSpace: 'nowrap' as const,
  },
  list: { display: 'flex', flexDirection: 'column', gap: '0.75rem' },
  card: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '1rem',
    border: '1px solid #2e303a',
  },
  cardHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '0.75rem',
  },
  project: {
    fontSize: '0.8rem',
    color: '#60a5fa',
    background: '#1e293b',
    padding: '2px 8px',
    borderRadius: 4,
    fontFamily: 'monospace',
  },
  date: { fontSize: '0.8rem', color: '#6b7280' },
  description: {
    color: '#f3f4f6',
    fontSize: '0.95rem',
    marginBottom: '0.75rem',
    lineHeight: 1.4,
  },
  detail: {
    marginBottom: '0.4rem',
  },
  detailLabel: {
    fontSize: '0.8rem',
    color: '#9ca3af',
    marginRight: '0.5rem',
  },
  detailValue: {
    fontSize: '0.85rem',
    color: '#d1d5db',
  },
  filesSection: {
    marginTop: '0.5rem',
  },
  fileList: {
    display: 'flex',
    flexWrap: 'wrap' as const,
    gap: '0.35rem',
    marginTop: '0.25rem',
  },
  fileTag: {
    fontSize: '0.75rem',
    background: '#2a2a4a',
    color: '#a5b4fc',
    padding: '2px 6px',
    borderRadius: 3,
    fontFamily: 'monospace',
  },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
  empty: { color: '#6b7280', textAlign: 'center' as const, padding: '2rem' },
};
