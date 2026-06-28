import { createContext, useContext, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import {
  api,
  cancelProactiveRefresh,
  scheduleProactiveRefresh,
  setAccessToken,
  setUnauthorizedHandler,
  type UserDto,
} from "./api";

interface AuthState {
  user: UserDto | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<void>;
  registerFirst: (username: string, email: string | null, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setUnauthorizedHandler(() => {
      cancelProactiveRefresh();
      setAccessToken(null);
      setUser(null);
    });
  }, []);

  // On boot, try to silently restore a session via the refresh cookie.
  useEffect(() => {
    (async () => {
      try {
        const res = await api.refresh();
        setAccessToken(res.accessToken);
        scheduleProactiveRefresh(res.expiresInSeconds);
        setUser(res.user);
      } catch {
        setAccessToken(null);
        setUser(null);
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const login = async (username: string, password: string) => {
    const res = await api.login(username, password);
    setAccessToken(res.accessToken);
    scheduleProactiveRefresh(res.expiresInSeconds);
    setUser(res.user);
  };

  const registerFirst = async (username: string, email: string | null, password: string) => {
    const res = await api.registerFirst(username, email, password);
    setAccessToken(res.accessToken);
    scheduleProactiveRefresh(res.expiresInSeconds);
    setUser(res.user);
  };

  const logout = async () => {
    try {
      await api.logout();
    } finally {
      cancelProactiveRefresh();
      setAccessToken(null);
      setUser(null);
    }
  };

  const value = useMemo<AuthState>(
    () => ({ user, loading, login, registerFirst, logout }),
    [user, loading]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
