import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'warning' | 'info';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

/**
 * Transient messages.
 *
 * IMSAngular uses ngx-toastr. This is a small signal-backed replacement so the
 * app does not take a dependency for something a Bootstrap toast markup block
 * already handles — and so it renders correctly under SSR.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private static nextId = 0;
  private static readonly DefaultDurationMs = 5000;

  private readonly items = signal<Toast[]>([]);

  readonly toasts = this.items.asReadonly();

  success(message: string): void {
    this.show('success', message);
  }

  error(message: string): void {
    this.show('error', message);
  }

  warning(message: string): void {
    this.show('warning', message);
  }

  info(message: string): void {
    this.show('info', message);
  }

  dismiss(id: number): void {
    this.items.update(list => list.filter(toast => toast.id !== id));
  }

  private show(kind: ToastKind, message: string): void {
    const id = ToastService.nextId++;

    this.items.update(list => [...list, { id, kind, message }]);

    // Errors stay until dismissed. An error that vanishes after five seconds
    // is one the customer may never have read, and it is usually the message
    // that matters most.
    if (kind !== 'error') {
      setTimeout(() => this.dismiss(id), ToastService.DefaultDurationMs);
    }
  }
}
