import { useEffect, useState } from 'react';
import { api, type SessionSummary } from '../api/client';
import MarkdownContent from '../components/MarkdownContent';

const phaseColors: Record<string, string> = {
  Exploration: '#3b82f6',
  Implementation: '#22c55e',
  Debugging: '#ef4444',
  Refactoring: '#eab308',
};

export default function Sessions() {
  const [sessions, setSessions] = useState<SessionSummary[]>([]);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .sessions()
      .then((data) => {
        setSessions(data);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  }, []);

  const copyMarkdown = (session: SessionSummary) => {
    const md = `## Session Story\n\n${session.narrative}\n\n**Outcome:** ${session.outcome}\n\n_${session.observationCount} observations | ${session.filesTouched} files | ${session.deadEndsHit} dead ends_`;
    navigator.clipboard.writeText(md);
  };

  if (error) return <div style={styles.error}>Error: {error}</div>;
  if (loading) return <div style={styles.loading}>Loading sessions...</div>;

  return (
    <div style={styles.container}>
      <h1>Sessions</h1>

      {sessions.length === 0 && (
        <p style={styles.empty}>No session stories generated yet.</p>
      )}

      <div style={styles.list}>
        {sessions.map((session) => (
          <div key={session.id} style={styles.card}>
            <div style={styles.cardHeader}>
              <div style={styles.stats}>
                <span style={styles.stat}>{session.observationCount} obs</span>
                <span style={styles.stat}>{session.filesTouched} files</span>
                {session.deadEndsHit > 0 && (
                  <span style={{ ...styles.stat, color: '#ef4444' }}>
                    {session.deadEndsHit} dead ends
                  </span>
                )}
              </div>
              <span style={styles.date}>
                {new Date(session.createdAt).toLocaleDateString()}
              </span>
            </div>

            {/* Phase bar + labels */}
            {session.phases.length > 0 && (
              <>
                <div style={styles.phaseBar}>
                  {session.phases.map((phase, i) => (
                    <div
                      key={i}
                      style={{
                        ...styles.phaseSegment,
                        background: phaseColors[phase] || '#6b7280',
                        flex: 1,
                      }}
                      title={phase}
                    />
                  ))}
                </div>
                <div style={styles.phaseLabels}>
                  {session.phases.map((phase, i) => (
                    <span
                      key={i}
                      style={{
                        ...styles.phaseLabel,
                        color: phaseColors[phase] || '#6b7280',
                      }}
                    >
                      {phase}
                    </span>
                  ))}
                </div>
              </>
            )}

            <div style={styles.outcome}>
              <MarkdownContent content={session.outcome} />
            </div>

            <div style={styles.actions}>
              <button
                style={styles.expandBtn}
                onClick={() =>
                  setExpanded(expanded === session.id ? null : session.id)
                }
              >
                {expanded === session.id ? 'Collapse' : 'Read Story'}
              </button>
              <button
                style={styles.copyBtn}
                onClick={() => copyMarkdown(session)}
              >
                Copy as Markdown
              </button>
            </div>

            {expanded === session.id && (
              <div style={{ marginTop: '1rem' }}>
                <MarkdownContent content={session.narrative} collapsible collapseAfterLines={15} />
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
  stats: { display: 'flex', gap: '0.75rem' },
  stat: {
    fontSize: '0.8rem',
    color: '#9ca3af',
    background: '#1e293b',
    padding: '2px 8px',
    borderRadius: 4,
    fontFamily: 'monospace',
  },
  date: { fontSize: '0.8rem', color: '#6b7280' },
  phaseBar: {
    display: 'flex',
    height: 6,
    borderRadius: 3,
    overflow: 'hidden',
    gap: 2,
    marginBottom: '0.35rem',
  },
  phaseSegment: { borderRadius: 2 },
  phaseLabels: {
    display: 'flex',
    gap: '0.5rem',
    marginBottom: '0.75rem',
  },
  phaseLabel: {
    fontSize: '0.7rem',
    fontWeight: 600,
    textTransform: 'uppercase' as const,
  },
  outcome: {
    color: '#d1d5db',
    fontSize: '0.9rem',
    marginBottom: '0.75rem',
    lineHeight: 1.4,
  },
  actions: { display: 'flex', gap: '0.5rem' },
  expandBtn: {
    padding: '0.35rem 1rem',
    background: '#2a2a4a',
    color: '#a5b4fc',
    border: '1px solid #3b3b6b',
    borderRadius: 4,
    cursor: 'pointer',
    fontSize: '0.8rem',
  },
  copyBtn: {
    padding: '0.35rem 1rem',
    background: '#374151',
    color: '#d1d5db',
    border: '1px solid #4b5563',
    borderRadius: 4,
    cursor: 'pointer',
    fontSize: '0.8rem',
  },
  narrative: {
    marginTop: '1rem',
    padding: '1rem',
    background: '#161620',
    borderRadius: 6,
    color: '#e5e7eb',
    fontSize: '0.9rem',
    lineHeight: 1.6,
    whiteSpace: 'pre-wrap' as const,
  },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
  empty: { color: '#6b7280', textAlign: 'center' as const, padding: '2rem' },
};
