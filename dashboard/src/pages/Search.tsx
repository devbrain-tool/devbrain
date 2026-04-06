import { useState } from 'react';
import { api, type Observation } from '../api/client';

export default function Search() {
  const [query, setQuery] = useState('');
  const [mode, setMode] = useState<'semantic' | 'exact'>('semantic');
  const [results, setResults] = useState<Observation[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [searched, setSearched] = useState(false);

  const handleSearch = () => {
    if (!query.trim()) return;
    setLoading(true);
    setError(null);
    const fn = mode === 'semantic' ? api.search : api.searchExact;
    fn(query.trim(), 20)
      .then((data) => {
        setResults(data);
        setSearched(true);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleSearch();
  };

  return (
    <div style={styles.container}>
      <h1>Search</h1>

      <div style={styles.controls}>
        <input
          type="text"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Search observations..."
          style={styles.input}
        />
        <button onClick={handleSearch} disabled={loading} style={styles.button}>
          {loading ? 'Searching...' : 'Search'}
        </button>
      </div>

      <div style={styles.toggle}>
        <label style={styles.toggleLabel}>
          <input
            type="radio"
            name="mode"
            checked={mode === 'semantic'}
            onChange={() => setMode('semantic')}
          />{' '}
          Semantic
        </label>
        <label style={styles.toggleLabel}>
          <input
            type="radio"
            name="mode"
            checked={mode === 'exact'}
            onChange={() => setMode('exact')}
          />{' '}
          Exact
        </label>
      </div>

      {error && <div style={styles.error}>Error: {error}</div>}

      {searched && results.length === 0 && !loading && (
        <p style={styles.empty}>No results found.</p>
      )}

      <div style={styles.results}>
        {results.map((obs, idx) => (
          <div key={obs.id} style={styles.card}>
            <div style={styles.cardHeader}>
              <span style={styles.rank}>#{idx + 1}</span>
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
          </div>
        ))}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 800, margin: '0 auto' },
  controls: { display: 'flex', gap: '0.5rem', marginBottom: '0.75rem' },
  input: {
    flex: 1,
    padding: '0.6rem 1rem',
    background: '#1f2028',
    border: '1px solid #2e303a',
    borderRadius: 6,
    color: '#f3f4f6',
    fontSize: '1rem',
    outline: 'none',
  },
  button: {
    padding: '0.6rem 1.25rem',
    background: '#2a2a4a',
    color: '#e0e0ff',
    border: '1px solid #3b3b6b',
    borderRadius: 6,
    cursor: 'pointer',
    fontSize: '0.9rem',
  },
  toggle: { display: 'flex', gap: '1rem', marginBottom: '1.5rem' },
  toggleLabel: { color: '#9ca3af', fontSize: '0.9rem', cursor: 'pointer' },
  results: { display: 'flex', flexDirection: 'column', gap: '0.75rem' },
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
  },
  rank: { color: '#c084fc', fontWeight: 700, fontSize: '0.85rem' },
  eventType: {
    fontWeight: 600,
    color: '#c084fc',
    fontSize: '0.8rem',
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
  error: { padding: '1rem', color: '#ef4444' },
  empty: { color: '#6b7280', textAlign: 'center' as const, padding: '2rem' },
};
