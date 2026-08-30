import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="container py-5 text-center">
      <h1 class="display-6">Page not found</h1>
      <p class="text-muted">That page does not exist, or it has moved.</p>
      <a class="btn btn-dark" routerLink="/">Back to the store</a>
    </div>
  `
})
export class NotFound {}
