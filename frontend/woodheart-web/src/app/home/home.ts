import { Component, ChangeDetectionStrategy, inject, signal, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DiagnosticsService } from '../_services/diagnostics.service';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="py-5 bg-light border-bottom">
      <div class="container text-center">
        <h1 class="display-5 fw-semibold">Interiors, made in Bangladesh</h1>
        <p class="lead text-muted mb-4">
          Beds, wardrobes, dining sets, mirrors and lighting — plus interior design consultation.
        </p>
        <a class="btn btn-dark btn-lg" routerLink="/products">Browse the collection</a>
      </div>
    </section>

    <section class="container py-5">
      <!-- Phase 0 placeholder. Proves the API round trip end to end, and gets
           replaced by the featured-products grid in Phase 1. -->
      <div class="card">
        <div class="card-body">
          <h6 class="card-title">API connection</h6>

          @if (apiStatus(); as status) {
            <p class="mb-1 small text-success">Connected — {{ status.environment }}</p>
            <p class="mb-0 small text-muted">Dhaka time: {{ status.dhakaNow }}</p>
          } @else if (failed()) {
            <p class="mb-0 small text-danger">
              Could not reach the API. Is it running on localhost:5199?
            </p>
          } @else {
            <p class="mb-0 small text-muted">Checking…</p>
          }
        </div>
      </div>
    </section>
  `
})
export class Home implements OnInit {
  private readonly diagnostics = inject(DiagnosticsService);

  protected readonly apiStatus = signal<{ environment: string; dhakaNow: string } | null>(null);
  protected readonly failed = signal(false);

  ngOnInit(): void {
    this.diagnostics.ping().subscribe({
      next: response => this.apiStatus.set(response),
      error: () => this.failed.set(true)
    });
  }
}
