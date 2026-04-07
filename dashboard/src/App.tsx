import { BrowserRouter, Routes, Route } from 'react-router-dom';
import Navigation from './components/Navigation';
import Timeline from './pages/Timeline';
import Briefings from './pages/Briefings';
import DeadEnds from './pages/DeadEnds';
import Threads from './pages/Threads';
import Search from './pages/Search';
import SettingsPage from './pages/SettingsPage';
import Health from './pages/Health';
import Database from './pages/Database';

export default function App() {
  return (
    <BrowserRouter>
      <Navigation />
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
        </Routes>
      </main>
    </BrowserRouter>
  );
}
