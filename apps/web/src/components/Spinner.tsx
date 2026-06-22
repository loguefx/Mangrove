export function Spinner({ label }: { label?: string }) {
  return (
    <div className="flex flex-col items-center gap-3 text-neutral-400">
      <div className="h-8 w-8 animate-spin rounded-full border-2 border-teal-mint border-t-transparent" />
      {label && <span className="text-sm">{label}</span>}
    </div>
  );
}
