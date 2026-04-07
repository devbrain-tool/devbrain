import { NavLink } from 'react-router-dom';

const links = [
  { to: '/', label: 'Timeline' },
  { to: '/briefings', label: 'Briefings' },
  { to: '/dead-ends', label: 'Dead Ends' },
  { to: '/alerts', label: 'Alerts' },
  { to: '/sessions', label: 'Sessions' },
  { to: '/threads', label: 'Threads' },
  { to: '/search', label: 'Search' },
  { to: '/settings', label: 'Settings' },
  { to: '/database', label: 'Database' },
  { to: '/setup', label: 'Setup' },
  { to: '/health', label: 'Health' },
];

export default function Navigation() {
  return (
    <nav style={styles.nav}>
      <span style={styles.brand}>DevBrain</span>
      <div style={styles.links}>
        {links.map(({ to, label }) => (
          <NavLink
            key={to}
            to={to}
            style={({ isActive }) => ({
              ...styles.link,
              ...(isActive ? styles.activeLink : {}),
            })}
          >
            {label}
          </NavLink>
        ))}
      </div>
    </nav>
  );
}

const styles: Record<string, React.CSSProperties> = {
  nav: {
    display: 'flex',
    alignItems: 'center',
    gap: '1.5rem',
    padding: '0.75rem 1.5rem',
    borderBottom: '1px solid #333',
    background: '#1a1a2e',
  },
  brand: {
    fontWeight: 700,
    fontSize: '1.25rem',
    color: '#e0e0ff',
    marginRight: '1rem',
  },
  links: {
    display: 'flex',
    gap: '0.5rem',
    flexWrap: 'wrap' as const,
  },
  link: {
    color: '#9ca3af',
    textDecoration: 'none',
    padding: '0.35rem 0.75rem',
    borderRadius: '4px',
    fontSize: '0.9rem',
    transition: 'background 0.15s',
  },
  activeLink: {
    color: '#e0e0ff',
    background: '#2a2a4a',
  },
};
