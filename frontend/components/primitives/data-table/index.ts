// Data table primitives — shared shell, sortable headers, clickable
// rows, skeleton, and empty state. Every transactional/report table
// should compose these instead of rolling its own Th/Td/SortableTh
// helpers so visual rhythm (padding, header tracking, hover, focus
// ring) and accessibility (scope, aria-sort, keyboard nav) stay
// uniform across the dashboard.

export {
  DataTableShell,
  DataTableHead,
  DataTableBody,
  TableTh,
  TableTd,
  type TableDensity,
} from "./table-shell";
export { SortableTh } from "./sortable-th";
export { DataRow } from "./data-row";
export { TableSkeleton } from "./table-skeleton";
export {
  TableEmptyState,
  resolveEmptyStateVariant,
  type EmptyStateVariant,
} from "./table-empty-state";
