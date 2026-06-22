import iconUrl from "../assets/mangrove-icon.svg";
import wordmarkUrl from "../assets/mangrove-wordmark.svg";

export function MangroveIcon({ className }: { className?: string }) {
  return <img src={iconUrl} alt="Mangrove" className={className} />;
}

export function MangroveWordmark({ className }: { className?: string }) {
  return <img src={wordmarkUrl} alt="Mangrove" className={className} />;
}
