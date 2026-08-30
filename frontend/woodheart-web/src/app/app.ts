import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { BusyService } from './_services/busy.service';
import { ToastService } from './_services/toast.service';

/**
 * The shell. Holds only the things that must sit above every layout: the
 * router outlet, the global spinner and the toast host.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (busy.isBusy()) {
      <div class="wh-busy" role="status" aria-live="polite">
        <div class="spinner-border text-dark" aria-hidden="true"></div>
        <span class="visually-hidden">Loading</span>
      </div>
    }

    <router-outlet />

    <div class="toast-container position-fixed bottom-0 end-0 p-3">
      @for (item of toast.toasts(); track item.id) {
        <div
          class="toast show align-items-center border-0 mb-2"
          [class.text-bg-success]="item.kind === 'success'"
          [class.text-bg-danger]="item.kind === 'error'"
          [class.text-bg-warning]="item.kind === 'warning'"
          [class.text-bg-secondary]="item.kind === 'info'"
          role="alert"
          aria-live="assertive">
          <div class="d-flex">
            <div class="toast-body">{{ item.message }}</div>
            <button
              class="btn-close btn-close-white me-2 m-auto"
              type="button"
              aria-label="Close"
              (click)="toast.dismiss(item.id)"></button>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    .wh-busy {
      position: fixed;
      inset: 0;
      z-index: 2000;
      display: grid;
      place-items: center;
      background: rgb(255 255 255 / 55%);
      /* Clicks must not reach the page underneath while a request is running —
         a second submit on a checkout is a duplicate order. */
      pointer-events: all;
    }
  `
})
export class App {
  protected readonly busy = inject(BusyService);
  protected readonly toast = inject(ToastService);
}
