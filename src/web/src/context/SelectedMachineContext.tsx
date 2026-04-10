import { createContext, useContext, useEffect, useMemo, useState, type PropsWithChildren } from "react";
import { api } from "../api/client";
import { usePollingAsyncData } from "../hooks/usePollingAsyncData";
import type { MachineRecord } from "../types/api";

const selectedMachineStorageKey = "gamarr:selected-machine-id";

interface SelectedMachineContextValue {
  machines: MachineRecord[];
  allMachines: MachineRecord[];
  selectedMachineId: string;
  selectedMachine: MachineRecord | null;
  duplicateCount: number;
  loading: boolean;
  error: string | null;
  setSelectedMachineId: (machineId: string) => void;
  reloadMachines: () => Promise<void>;
}

const SelectedMachineContext = createContext<SelectedMachineContextValue | null>(null);

export function SelectedMachineProvider({ children }: PropsWithChildren) {
  const machineState = usePollingAsyncData(() => api.listMachines(), [], 10000, true);
  const allMachines = machineState.data ?? [];
  const [selectedMachineId, setSelectedMachineId] = useState("");

  const machines = useMemo(() => {
    const groups = new Map<string, MachineRecord[]>();
    for (const machine of allMachines) {
      const key = `${machine.hostname.trim().toLowerCase()}::${machine.name.trim().toLowerCase()}`;
      const group = groups.get(key) ?? [];
      group.push(machine);
      groups.set(key, group);
    }

    return Array.from(groups.values())
      .map((group) =>
        [...group].sort((left, right) => {
          const leftOnline = left.status === "Online" || left.status === "Busy" ? 1 : 0;
          const rightOnline = right.status === "Online" || right.status === "Busy" ? 1 : 0;
          if (leftOnline !== rightOnline) {
            return rightOnline - leftOnline;
          }

          const heartbeatDelta = new Date(right.lastHeartbeatUtc).getTime() - new Date(left.lastHeartbeatUtc).getTime();
          if (heartbeatDelta !== 0) {
            return heartbeatDelta;
          }

          return new Date(right.registeredAtUtc).getTime() - new Date(left.registeredAtUtc).getTime();
        })[0]
      )
      .sort((left, right) => left.name.localeCompare(right.name));
  }, [allMachines]);

  useEffect(() => {
    const storedMachineId = window.localStorage.getItem(selectedMachineStorageKey);
    if (storedMachineId) {
      setSelectedMachineId(storedMachineId);
    }
  }, []);

  useEffect(() => {
    if (!machines.length) {
      return;
    }

    const selectedStillExists = machines.some((machine) => machine.id === selectedMachineId);
    if (!selectedStillExists) {
      const freshestMachine = [...machines].sort(
        (left, right) => new Date(right.lastHeartbeatUtc).getTime() - new Date(left.lastHeartbeatUtc).getTime()
      )[0];

      if (freshestMachine) {
        setSelectedMachineId(freshestMachine.id);
      }
    }
  }, [machines, selectedMachineId]);

  useEffect(() => {
    if (selectedMachineId) {
      window.localStorage.setItem(selectedMachineStorageKey, selectedMachineId);
    }
  }, [selectedMachineId]);

  const value = useMemo<SelectedMachineContextValue>(() => {
    const selectedMachine = machines.find((machine) => machine.id === selectedMachineId) ?? null;
    return {
      machines,
      allMachines,
      selectedMachineId,
      selectedMachine,
      duplicateCount: Math.max(0, allMachines.length - machines.length),
      loading: machineState.loading,
      error: machineState.error,
      setSelectedMachineId,
      reloadMachines: machineState.reload
    };
  }, [allMachines, machineState.error, machineState.loading, machineState.reload, machines, selectedMachineId]);

  return <SelectedMachineContext.Provider value={value}>{children}</SelectedMachineContext.Provider>;
}

export function useSelectedMachine() {
  const context = useContext(SelectedMachineContext);
  if (!context) {
    throw new Error("useSelectedMachine must be used within SelectedMachineProvider.");
  }

  return context;
}
