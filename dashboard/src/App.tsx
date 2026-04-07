import { BrowserRouter, Routes, Route } from 'react-router-dom';
import Navigation from './components/Navigation';
import AlertBanner from './components/AlertBanner';
import Timeline from './pages/Timeline';
import Briefings from './pages/Briefings';
import DeadEnds from './pages/DeadEnds';
import Threads from './pages/Threads';
import Search from './pages/Search';
import SettingsPage from './pages/SettingsPage';
import Health from './pages/Health';
import Database from './pages/Database';
import Setup from './pages/Setup';
import Alerts from './pages/Alerts';
import Sessions from './pages/Sessions';
import Replay from './pages/Replay';
import BlastRadius from './pages/BlastRadius';

export default function App() {
  return (
    <BrowserRouter>
      <Navigation />
      <AlertBanner />
      <main>
        <Routes>
          <Route path="/" element={<Timeline />} />
          <Route path="/briefings" element={<Briefings />} />
          <Route path="/dead-ends" element={<DeadEnds />} />
          <Route path="/threads" element={<Threads />} />
          <Route path="/search" element={<Search />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/health" element={<Health />} />
          <Route path="/database" element={<Database />} />
          <Route path="/setup" element={<Setup />} />
          <Route path="/alerts" element={<Alerts />} />
          <Route path="/sessions" element={<Sessions />} />
          <Route path="/replay" element={<Replay />} />
          <Route path="/blast-radius" element={<BlastRadius />} />
        </Routes>
      </main>
    </BrowserRouter>
  );
}
