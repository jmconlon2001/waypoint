import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'warning';
export interface Toast { id: number; kind: ToastKind; text: string; }

@Injectable({ providedIn: 'root' })
export class ToastService {
  private seq = 0;
  readonly toasts = signal<Toast[]>([]);

  show(text: string, kind: ToastKind = 'success', ms = 3200): void {
    const toast = { id: ++this.seq, kind, text };
    this.toasts.update((t) => [...t, toast]);
    setTimeout(() => this.dismiss(toast.id), kind === 'error' ? ms + 2500 : ms);
  }

  error(text: string): void { this.show(text, 'error'); }
  warning(text: string): void { this.show(text, 'warning', 5000); }

  dismiss(id: number): void {
    this.toasts.update((t) => t.filter((x) => x.id !== id));
  }
}
