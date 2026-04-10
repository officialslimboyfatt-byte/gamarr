import { Route, Routes } from "react-router-dom";
import { AppLayout } from "./layout/AppLayout";
import { JobsPage } from "./pages/JobsPage";
import { LibraryPage } from "./pages/LibraryPage";
import { LibraryTitleDetailPage } from "./pages/LibraryTitleDetailPage";
import { MachinesPage } from "./pages/MachinesPage";
import { PackageDetailPage } from "./pages/PackageDetailPage";
import { PackagesPage } from "./pages/PackagesPage";
import { MachineDetailPage } from "./pages/MachineDetailPage";
import { JobDetailPage } from "./pages/JobDetailPage";
import { ImportsPage } from "./pages/ImportsPage";
import { SelectedMachineProvider } from "./context/SelectedMachineContext";
import { ActivityQueuePage } from "./pages/ActivityQueuePage";
import { SettingsPage } from "./pages/SettingsPage";
import { HealthPage } from "./pages/HealthPage";
import { EventsPage } from "./pages/EventsPage";
import { LogsPage } from "./pages/LogsPage";
import { AddMachinePage } from "./pages/AddMachinePage";

export default function App() {
  return (
    <SelectedMachineProvider>
      <AppLayout>
        <Routes>
          <Route path="/" element={<LibraryPage />} />
          <Route path="/library/:id" element={<LibraryTitleDetailPage />} />
          <Route path="/imports" element={<ImportsPage />} />
          <Route path="/packages" element={<PackagesPage />} />
          <Route path="/packages/:id" element={<PackageDetailPage />} />
          <Route path="/machines" element={<MachinesPage />} />
          <Route path="/machines/add" element={<AddMachinePage />} />
          <Route path="/machines/:id" element={<MachineDetailPage />} />
          <Route path="/system/health" element={<HealthPage />} />
          <Route path="/system/events" element={<EventsPage />} />
          <Route path="/system/logs" element={<LogsPage />} />
          <Route path="/system/queue" element={<ActivityQueuePage />} />
          <Route path="/jobs" element={<JobsPage />} />
          <Route path="/jobs/:id" element={<JobDetailPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Routes>
      </AppLayout>
    </SelectedMachineProvider>
  );
}
