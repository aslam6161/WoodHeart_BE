import { Injectable, signal, computed } from '@angular/core';

/**
 * Tracks how many requests are in flight.
 *
 * A counter rather than a boolean, because two overlapping requests would
 * otherwise have the first one to finish switch the spinner off while the
 * second is still running.
 */
@Injectable({ providedIn: 'root' })
export class BusyService {
  private readonly requestCount = signal(0);

  readonly isBusy = computed(() => this.requestCount() > 0);

  busy(): void {
    this.requestCount.update(count => count + 1);
  }

  idle(): void {
    // Floored at zero: an unbalanced idle() must not push the count negative,
    // because a negative count makes the next genuine busy() a no-op.
    this.requestCount.update(count => (count > 0 ? count - 1 : 0));
  }

  reset(): void {
    this.requestCount.set(0);
  }
}
