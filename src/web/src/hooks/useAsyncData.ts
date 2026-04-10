import { DependencyList, Dispatch, SetStateAction, useCallback, useEffect, useRef, useState } from "react";

export interface ReloadOptions {
  background?: boolean;
}

export interface AsyncState<T> {
  data: T | null;
  loading: boolean;
  error: string | null;
  reload: (options?: ReloadOptions) => Promise<void>;
  setData: Dispatch<SetStateAction<T | null>>;
}

export function useAsyncData<T>(loader: () => Promise<T>, deps: DependencyList = []): AsyncState<T> {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const dataRef = useRef<T | null>(null);

  useEffect(() => {
    dataRef.current = data;
  }, [data]);

  const reload = useCallback(async (options?: ReloadOptions) => {
    const background = options?.background ?? false;
    const hasData = dataRef.current !== null;

    if (!background || !hasData) {
      setLoading(true);
    }

    if (!background) {
      setError(null);
    }

    try {
      const next = await loader();
      setData(next);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Request failed.");
    } finally {
      if (!background || !hasData) {
        setLoading(false);
      }
    }
  }, deps);

  useEffect(() => {
    void reload();
  }, [reload]);

  return { data, loading, error, reload, setData };
}
