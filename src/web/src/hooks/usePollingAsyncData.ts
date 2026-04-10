import { DependencyList, useEffect } from "react";
import { useAsyncData } from "./useAsyncData";

export function usePollingAsyncData<T>(
  loader: () => Promise<T>,
  deps: DependencyList = [],
  intervalMs?: number,
  enabled = true
) {
  const state = useAsyncData(loader, deps);

  useEffect(() => {
    if (!enabled || !intervalMs) {
      return;
    }

    const handle = window.setInterval(() => {
      void state.reload({ background: true });
    }, intervalMs);

    return () => window.clearInterval(handle);
  }, [enabled, intervalMs, state.reload]);

  return state;
}
