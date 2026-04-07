import { useEffect, useState } from 'react';
import { api } from '../api/client';
import { useNavigate } from 'react-router-dom';

export default function AlertBanner() {
  const [count, setCount] = useState(0);
  const [message, setMessage] = useState('');
  const navigate = useNavigate();

  useEffect(() => {
    const check = () => {
      api.alerts().then((alerts) => {
        setCount(alerts.length);
        if (alerts.length > 0) setMessage(alerts[0].message);
      }).catch(() => {});
    };

    check();
    const interval = setInterval(check, 10000);
    return () => clearInterval(interval);
  }, []);

  if (count === 0) return null;

  return (
    <div style={styles.banner} onClick={() => navigate('/alerts')}>
      <span style={styles.icon}>!</span>
      <span style={styles.text}>
        {count} active alert{count !== 1 ? 's' : ''}
        {message && ` — ${message.slice(0, 80)}${message.length > 80 ? '...' : ''}`}
      </span>
    </div>
  );
}

const styles: Record<string, React.CSSProperties> = {
  banner: {
    background: '#7c2d12',
    color: '#fbbf24',
    padding: '0.5rem 1.5rem',
    fontSize: '0.85rem',
    cursor: 'pointer',
    display: 'flex',
    alignItems: 'center',
    gap: '0.75rem',
  },
  icon: {
    background: '#fbbf24',
    color: '#7c2d12',
    borderRadius: '50%',
    width: '1.2rem',
    height: '1.2rem',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontWeight: 800,
    fontSize: '0.75rem',
    flexShrink: 0,
  },
  text: { lineHeight: 1.3 },
};
