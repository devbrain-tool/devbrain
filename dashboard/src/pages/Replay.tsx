import { useState } from 'react';
import { api, type DecisionChain } from '../api/client';
import MarkdownContent from '../components/MarkdownContent';

const stepColors: Record<string, string> = {
  Decision: '#22c55e',
  DeadEnd: '#ef4444',
  Error: '#eab308',
  Resolution: '#3b82f6',
};

export default function Replay() {
  const [query, setQuery] = useState('');
  const [chain, setChain] = useState<DecisionChain | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const search = async () => {
    if (!query.trim()) return;
    setLoading(true);
    setError(null);
    setChain(null);

    try {
      const result = await api.replayFile(query.trim());
      setChain(result);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e);
      setError(
        msg.includes('404') || msg.includes('Not Found')
          ? `No decision chain found for '${query}'`
          : `Error: ${msg}`
      );
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={styles.container}>
      <h1>Decision Replay</h1>
      <p style={styles.subtitle}>
        Enter a file path to see why it exists — the full chain of decisions,
        dead ends, and resolutions.
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
          {loading ? 'Loading...' : 'Trace'}
        </button>
      </div>

      {error && <div style={styles.error}>{error}</div>}

      {chain && (
        <div style={styles.result}>
          <MarkdownContent content={chain.narrative} />

          <div style={styles.timeline}>
            {chain.steps.map((step, i) => (
              <div key={i} style={styles.step}>
                <div style={styles.stepLine}>
                  <div
                    style={{
                      ...styles.stepDot,
                      background: stepColors[step.stepType] || '#6b7280',
                    }}
                  />
                  {i < chain.steps.length - 1 && (
                    <div style={styles.stepConnector} />
                  )}
                </div>
                <div style={styles.stepContent}>
                  <div style={styles.stepHeader}>
                    <span
                      style={{
                        ...styles.stepType,
                        color: stepColors[step.stepType] || '#6b7280',
                      }}
                    >
                      {step.stepType}
                    </span>
                    <span style={styles.stepTime}>
                      {new Date(step.timestamp).toLocaleString()}
                    </span>
                  </div>
                  <div style={styles.stepSummary}>{step.summary}</div>
                  {step.filesInvolved.length > 0 && (
                    <div style={styles.stepFiles}>
                      {step.filesInvolved.map((f) => (
                        <span key={f} style={styles.fileTag}>
                          {f}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
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
  result: {},
  narrative: {
    padding: '1rem',
    background: '#161620',
    borderRadius: 6,
    color: '#d1d5db',
    fontSize: '0.9rem',
    lineHeight: 1.5,
    marginBottom: '1.5rem',
  },
  timeline: { display: 'flex', flexDirection: 'column' },
  step: { display: 'flex', gap: '1rem' },
  stepLine: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    width: 20,
    flexShrink: 0,
  },
  stepDot: {
    width: 12,
    height: 12,
    borderRadius: '50%',
    flexShrink: 0,
  },
  stepConnector: {
    width: 2,
    flex: 1,
    background: '#374151',
    minHeight: 30,
  },
  stepContent: {
    flex: 1,
    paddingBottom: '1.5rem',
  },
  stepHeader: {
    display: 'flex',
    gap: '0.75rem',
    alignItems: 'center',
    marginBottom: '0.25rem',
  },
  stepType: {
    fontSize: '0.75rem',
    fontWeight: 700,
    textTransform: 'uppercase' as const,
  },
  stepTime: { fontSize: '0.75rem', color: '#6b7280' },
  stepSummary: { color: '#f3f4f6', fontSize: '0.9rem', lineHeight: 1.4 },
  stepFiles: {
    display: 'flex',
    flexWrap: 'wrap' as const,
    gap: '0.3rem',
    marginTop: '0.4rem',
  },
  fileTag: {
    fontSize: '0.7rem',
    background: '#2a2a4a',
    color: '#a5b4fc',
    padding: '2px 6px',
    borderRadius: 3,
    fontFamily: 'monospace',
  },
};
