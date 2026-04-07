import { useEffect, useState, useCallback } from 'react';
import { api, type DejaVuAlert } from '../api/client';

export default function Alerts() {
  const [alerts, setAlerts] = useState<DejaVuAlert[]>([]);
  const [showDismissed, setShowDismissed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadAlerts = useCallback(() => {
    setLoading(true);
    const fetcher = showDismissed ? api.alertsAll : api.alerts;
    fetcher()
      .then((data) => {
        setAlerts(data);
        setLoading(false);
      })
      .catch((e) => {
        setError(String(e));
        setLoading(false);
      });
  }, [showDismissed]);

  useEffect(() => {
    loadAlerts();
  }, [loadAlerts]);

  const handleDismiss = async (id: string) => {
    try {
      await api.alertDismiss(id);
      loadAlerts();
    } catch (e) {
      setError(String(e));
    }
  };

  if (error) return <div style={styles.error}>Error: {error}</div>;
  if (loading) return <div style={styles.loading}>Loading alerts...</div>;

  const activeCount = alerts.filter((a) => !a.dismissed).length;

  return (
    <div style={styles.container}>
      <h1>Deja Vu Alerts</h1>

      <div style={styles.controls}>
        <span style={styles.count}>
          {activeCount} active alert{activeCount !== 1 ? 's' : ''}
        </span>
        <label style={styles.toggle}>
          <input
            type="checkbox"
            checked={showDismissed}
            onChange={(e) => setShowDismissed(e.target.checked)}
          />
          Show dismissed
        </label>
      </div>

      {alerts.length === 0 && (
        <p style={styles.empty}>
          {showDismissed
            ? 'No alerts recorded yet.'
            : "No active alerts. You're in the clear!"}
        </p>
      )}

      <div style={styles.list}>
        {alerts.map((alert) => (
          <div
            key={alert.id}
            style={{
              ...styles.card,
              ...(alert.dismissed ? styles.cardDismissed : {}),
            }}
          >
            <div style={styles.cardHeader}>
              <div style={styles.badges}>
                <span
                  style={{
                    ...styles.badge,
                    background: alert.dismissed ? '#374151' : '#7c2d12',
                    color: alert.dismissed ? '#6b7280' : '#fbbf24',
                  }}
                >
                  {alert.dismissed ? 'Dismissed' : 'Active'}
                </span>
                <span style={styles.strategyBadge}>{alert.strategy}</span>
                <span style={styles.confidence}>
                  {Math.round(alert.confidence * 100)}% match
                </span>
              </div>
              <span style={styles.date}>
                {new Date(alert.createdAt).toLocaleString()}
              </span>
            </div>

            <div style={styles.message}>{alert.message}</div>

            {!alert.dismissed && (
              <button
                style={styles.dismissBtn}
                onClick={() => handleDismiss(alert.id)}
              >
                Dismiss
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  container: { padding: '1.5rem', maxWidth: 800, margin: '0 auto' },
  controls: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '1.5rem',
  },
  count: { fontSize: '0.9rem', color: '#fbbf24', fontWeight: 600 },
  toggle: { fontSize: '0.85rem', color: '#9ca3af', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '0.4rem' },
  list: { display: 'flex', flexDirection: 'column', gap: '0.75rem' },
  card: {
    background: '#1f2028',
    borderRadius: 8,
    padding: '1rem',
    border: '1px solid #7c2d12',
  },
  cardDismissed: {
    opacity: 0.5,
    borderColor: '#2e303a',
  },
  cardHeader: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '0.75rem',
    flexWrap: 'wrap' as const,
    gap: '0.5rem',
  },
  badges: { display: 'flex', gap: '0.5rem', alignItems: 'center' },
  badge: {
    fontSize: '0.7rem',
    padding: '2px 8px',
    borderRadius: 4,
    fontWeight: 600,
    textTransform: 'uppercase' as const,
  },
  strategyBadge: {
    fontSize: '0.7rem',
    background: '#1e293b',
    color: '#60a5fa',
    padding: '2px 8px',
    borderRadius: 4,
    fontFamily: 'monospace',
  },
  confidence: { fontSize: '0.8rem', color: '#9ca3af' },
  date: { fontSize: '0.8rem', color: '#6b7280' },
  message: {
    color: '#f3f4f6',
    fontSize: '0.9rem',
    lineHeight: 1.5,
  },
  dismissBtn: {
    marginTop: '0.75rem',
    padding: '0.35rem 1rem',
    background: '#374151',
    color: '#d1d5db',
    border: '1px solid #4b5563',
    borderRadius: 4,
    cursor: 'pointer',
    fontSize: '0.8rem',
  },
  loading: { padding: '2rem', textAlign: 'center' as const, color: '#9ca3af' },
  error: { padding: '2rem', textAlign: 'center' as const, color: '#ef4444' },
  empty: { color: '#6b7280', textAlign: 'center' as const, padding: '2rem' },
};
