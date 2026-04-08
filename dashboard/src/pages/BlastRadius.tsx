import { useState } from 'react';
import { api, type BlastRadius as BlastRadiusType } from '../api/client';

function riskColor(score: number): string {
  if (score > 0.7) return '#ef4444';
  if (score > 0.3) return '#eab308';
  return '#22c55e';
}

export default function BlastRadius() {
  const [query, setQuery] = useState('');
  const [result, setResult] = useState<BlastRadiusType | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const search = async () => {
    if (!query.trim()) return;
    setLoading(true);
    setError(null);
    setResult(null);

    try {
      const data = await api.blastRadius(query.trim());
      setResult(data);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={styles.container}>
      <h1>Blast Radius</h1>
      <p style={styles.subtitle}>
        Enter a file path to see what else might break if you change it —
        based on decision dependencies, not just code imports.
      </p>

      <div style={styles.searchRow}>
        <input
          type="text"
          placeholder="src/DevBrain.Storage/SqliteGraphStore.cs"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && search()}
          style={styles.input}
        />
        <button onClick={search} style={styles.searchBtn} disabled={loading}>
          {loading ? 'Analyzing...' : 'Analyze'}
        </button>
      </div>

      {error && <div style={styles.error}>{error}</div>}

      {result && (
        <div>
          {result.deadEndsAtRisk.length > 0 && (
            <div style={styles.deadEndWarning}>
              {result.deadEndsAtRisk.length} dead end(s) at risk of
              re-triggering
            </div>
          )}

          {result.affectedFiles.length === 0 ? (
            <p style={styles.safe}>
              No affected files found. Safe to change!
            </p>
          ) : (
            <div style={styles.fileList}>
              <div style={styles.fileListHeader}>
                {result.affectedFiles.length} affected file(s)
              </div>
              {result.affectedFiles.map((file) => (
                <div key={file.filePath} style={styles.fileCard}>
                  <div style={styles.fileHeader}>
                    <div style={styles.riskBadgeContainer}>
                      <div
                        style={{
                          ...styles.riskBar,
                          width: `${Math.round(file.riskScore * 100)}%`,
                          background: riskColor(file.riskScore),
                        }}
                      />
                      <span
                        style={{
                          ...styles.riskScore,
                          color: riskColor(file.riskScore),
                        }}
                      >
                        {(file.riskScore * 100).toFixed(0)}%
                      </span>
                    </div>
                    <span style={styles.chainLength}>
                      chain: {file.chainLength}
                    </span>
                  </div>
                  <div style={styles.filePath}>{file.filePath}</div>
                  <div style={styles.reason}>{file.reason}</div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 800, margin: '0 auto' },
  subtitle: { color: '#9ca3af', fontSize: '0.9rem', marginBottom: '1.5rem' },
  searchRow: { display: 'flex', gap: '0.5rem', marginBottom: '1.5rem' },
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
  searchBtn: {
    padding: '0.5rem 1.25rem',
    background: '#2a2a4a',
    color: '#a5b4fc',
    border: '1px solid #3b3b6b',
    borderRadius: 6,
    cursor: 'pointer',
    fontSize: '0.9rem',
  },
  error: { color: '#ef4444', marginBottom: '1rem' },
  deadEndWarning: {
    background: '#7c2d12',
    color: '#fbbf24',
    padding: '0.75rem 1rem',
    borderRadius: 6,
    marginBottom: '1rem',
    fontSize: '0.9rem',
    fontWeight: 600,
  },
  safe: {
    color: '#22c55e',
    textAlign: 'center' as const,
    padding: '2rem',
    fontSize: '1.1rem',
  },
  fileList: {},
  fileListHeader: {
    color: '#9ca3af',
    fontSize: '0.85rem',
    marginBottom: '0.75rem',
  },
  fileCard: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '0.75rem 1rem',
    border: '1px solid #2e303a',
    marginBottom: '0.5rem',
  },
  fileHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '0.35rem',
  },
  riskBadgeContainer: {
    display: 'flex',
    alignItems: 'center',
    gap: '0.5rem',
    flex: 1,
  },
  riskBar: {
    height: 6,
    borderRadius: 3,
    maxWidth: 100,
  },
  riskScore: { fontSize: '0.8rem', fontWeight: 700 },
  chainLength: {
    fontSize: '0.75rem',
    color: '#6b7280',
    fontFamily: 'monospace',
  },
  filePath: {
    color: '#a5b4fc',
    fontFamily: 'monospace',
    fontSize: '0.85rem',
    marginBottom: '0.25rem',
  },
  reason: { color: '#9ca3af', fontSize: '0.8rem' },
};
