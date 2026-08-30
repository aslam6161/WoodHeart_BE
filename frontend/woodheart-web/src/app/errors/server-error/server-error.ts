import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

@Component({
  selector: 'app-server-error',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container py-5">
      <h1 class="display-6">Something went wrong</h1>
      <p class="text-muted">
        This is our fault, not yours. The problem has been logged and we are looking at it.
      </p>

      @if (correlationId) {
        <!-- Shown deliberately: this is the id that lets support find the exact
             request in the logs. It is the difference between "the site broke"
             and a diagnosable report. -->
        <p class="small">
          If you contact us, quote this reference:
          <code>{{ correlationId }}</code>
        </p>
      }

      <a class="btn btn-dark" routerLink="/">Back to the store</a>
    </div>
  `
})
export class ServerError {
  private readonly router = inject(Router);

  protected readonly correlationId: string | null =
    this.router.getCurrentNavigation()?.extras.state?.['correlationId'] ?? null;
}
