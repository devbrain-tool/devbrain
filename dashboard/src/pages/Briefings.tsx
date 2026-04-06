import { useEffect, useState } from 'react';
import { api, type BriefingLatest } from '../api/client';

export default function Briefings() {
  const [files, setFiles] = useState<string[]>([]);
  const [current, setCurrent] = useState<BriefingLatest | null>(null);
  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.briefings(), api.briefingLatest()])
      .then(([fileList, latest]) => {
        setFiles(fileList);
        setCurrent(latest);
        setLoading(false);
      })
      .catch(() => {
        // briefingLatest may 404 if no briefings exist
        api.briefings()
          .then((fileList) => {
            setFiles(fileList);
            setLoading(false);
          })
          .catch((e2) => {
            setError(String(e2));
            setLoading(false);
          });
      });
  }, []);

  const handleGenerate = () => {
    setGenerating(true);
    api
      .briefingGenerate()
      .then(() => {
        setGenerating(false);
        // Reload after a short delay to allow generation
        setTimeout(() => {
          api.briefingLatest().then(setCurrent).catch(() => {});
          api.briefings().then(setFiles).catch(() => {});
        }, 2000);
      })
      .catch((e) => {
        setError(String(e));
        setGenerating(false);
      });
  };

  const handleSelectDate = (filename: string) => {
    // The filename is like "2026-04-05.md", load it via latest or construct
    // Since there's no endpoint for a specific date, we use the filename info
    // For now, if it's the current file, just keep it; otherwise show the filename
    // The server only has /latest, so we display what we have
    setCurrent({ file: filename, content: `Loading ${filename}...` });
    // Re-fetch latest to see if it matches
    api.briefingLatest().then((latest) => {
      if (latest.file === filename) {
        setCurrent(latest);
      } else {
        setCurrent({ file: filename, content: '(Briefing content not available via current API - only latest is served)' });
      }
    }).catch(() => {
      setCurrent({ file: filename, content: '(Could not load briefing)' });
    });
  };

  if (error) return <div style={styles.error}>Error: {error}</div>;
  if (loading) return <div style={styles.loading}>Loading briefings...</div>;

  return (
    <div style={styles.container}>
      <div style={styles.header}>
        <h1>Briefings</h1>
        <button
          onClick={handleGenerate}
          disabled={generating}
          style={{
            ...styles.button,
            opacity: generating ? 0.6 : 1,
            cursor: generating ? 'not-allowed' : 'pointer',
          }}
        >
          {generating ? 'Generating...' : 'Generate Now'}
        </button>
      </div>

      <div style={styles.layout}>
        {/* Date list sidebar */}
        <div style={styles.sidebar}>
          <h3 style={styles.sidebarTitle}>Available Dates</h3>
          {files.length === 0 && (
            <p style={styles.empty}>No briefings yet</p>
          )}
          {files.map((f) => {
            const isActive = current?.file === f;
            return (
              <button
                key={f}
                onClick={() => handleSelectDate(f)}
                style={{
                  ...styles.dateItem,
                  background: isActive ? '#2a2a4a' : 'transparent',
                  borderColor: isActive ? '#6366f1' : '#2e303a',
                }}
              >
                {f.replace('.md', '')}
              </button>
            );
          })}
        </div>

        {/* Briefing content */}
        <div style={styles.content}>
          {current ? (
            <>
              <div style={styles.contentHeader}>
                <span style={styles.dateLabel}>{current.file.replace('.md', '')}</span>
              </div>
              <pre style={styles.pre}>{current.content}</pre>
            </>
          ) : (
            <p style={styles.empty}>
              No briefings available. Click "Generate Now" to create one.
            </p>
          )}
        </div>
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 1000, margin: '0 auto' },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '1.5rem',
  },
  button: {
    padding: '0.5rem 1.25rem',
    background: '#4f46e5',
    color: '#fff',
    border: 'none',
    borderRadius: 6,
    fontSize: '0.9rem',
    fontWeight: 600,
  },
  layout: {
    display: 'flex',
    gap: '1.5rem',
  },
  sidebar: {
    width: 180,
    flexShrink: 0,
  },
  sidebarTitle: {
    fontSize: '0.85rem',
    color: '#9ca3af',
    marginBottom: '0.75rem',
    textTransform: 'uppercase' as const,
    letterSpacing: '0.05em',
  },
  dateItem: {
    display: 'block',
    width: '100%',
    padding: '0.5rem 0.75rem',
    marginBottom: '0.25rem',
    border: '1px solid #2e303a',
    borderRadius: 4,
    color: '#e0e0ff',
    fontSize: '0.85rem',
    fontFamily: 'monospace',
    textAlign: 'left' as const,
    cursor: 'pointer',
  },
  content: {
    flex: 1,
    minWidth: 0,
  },
  contentHeader: {
    marginBottom: '0.75rem',
  },
  dateLabel: {
    fontSize: '0.9rem',
    color: '#60a5fa',
    fontFamily: 'monospace',
  },
  pre: {
    background: '#1f2028',
    border: '1px solid #2e303a',
    borderRadius: 8,
    padding: '1.25rem',
    color: '#d1d5db',
    fontSize: '0.85rem',
    lineHeight: 1.6,
    fontFamily: 'monospace',
    whiteSpace: 'pre-wrap' as const,
    wordBreak: 'break-word' as const,
    overflow: 'auto',
    maxHeight: '70vh',
  },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
  empty: { color: '#6b7280', textAlign: 'center' as const, padding: '2rem' },
};
