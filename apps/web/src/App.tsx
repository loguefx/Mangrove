import { useEffect, useState } from "react";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import { api } from "./api";
import { useAuth } from "./auth";
import { Spinner } from "./components/Spinner";
import Setup from "./pages/Setup";
import Login from "./pages/Login";
import Dashboard from "./pages/Dashboard";
import SeriesPage from "./pages/SeriesPage";
import SearchPage from "./pages/Search";
import Reader from "./pages/Reader";
import WantToRead from "./pages/WantToRead";
import Collections from "./pages/Collections";
import CollectionDetail from "./pages/CollectionDetail";
import ReadingLists from "./pages/ReadingLists";
import ReadingListDetail from "./pages/ReadingListDetail";
import Admin from "./pages/Admin";

export default function App() {
  const { user, loading } = useAuth();
  const [setupComplete, setSetupComplete] = useState<boolean | null>(null);
  const location = useLocation();

  useEffect(() => {
    api
      .setupStatus()
      .then((s) => setSetupComplete(s.setupComplete))
      .catch(() => setSetupComplete(true));
  }, [user]);

  if (loading || setupComplete === null) {
    return (
      <div className="flex h-full items-center justify-center">
        <Spinner />
      </div>
    );
  }

  if (!setupComplete && location.pathname !== "/setup") {
    return <Navigate to="/setup" replace />;
  }

  return (
    <Routes>
      <Route path="/setup" element={setupComplete ? <Navigate to="/" replace /> : <Setup />} />
      <Route path="/login" element={user ? <Navigate to="/" replace /> : <Login />} />
      <Route path="/" element={user ? <Dashboard /> : <Navigate to="/login" replace />} />
      <Route path="/series/:id" element={user ? <SeriesPage /> : <Navigate to="/login" replace />} />
      <Route path="/search" element={user ? <SearchPage /> : <Navigate to="/login" replace />} />
      <Route path="/favorites" element={user ? <WantToRead /> : <Navigate to="/login" replace />} />
      <Route path="/want-to-read" element={<Navigate to="/favorites" replace />} />
      <Route path="/collections" element={user ? <Collections /> : <Navigate to="/login" replace />} />
      <Route path="/collections/:id" element={user ? <CollectionDetail /> : <Navigate to="/login" replace />} />
      <Route path="/reading-lists" element={user ? <ReadingLists /> : <Navigate to="/login" replace />} />
      <Route path="/reading-lists/:id" element={user ? <ReadingListDetail /> : <Navigate to="/login" replace />} />
      <Route
        path="/admin"
        element={user?.roles.includes("Admin") ? <Admin /> : <Navigate to="/" replace />}
      />
      <Route
        path="/reader/:chapterId"
        element={user ? <Reader /> : <Navigate to="/login" replace />}
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
