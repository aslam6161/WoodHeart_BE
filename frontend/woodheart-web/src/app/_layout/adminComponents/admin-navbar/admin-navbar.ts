import { Component, ChangeDetectionStrategy, inject, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AccountService } from '../../../_services/account.service';

@Component({
  selector: 'app-admin-navbar',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="navbar bg-white border-bottom px-3">
      <div class="d-flex align-items-center gap-3 w-100">
        <button
          class="btn btn-outline-secondary btn-sm"
          type="button"
          aria-label="Toggle sidebar"
          (click)="toggleSidebar.emit()">
          ☰
        </button>

        <span class="fw-semibold">WoodHeart Admin</span>

        <div class="ms-auto d-flex align-items-center gap-3">
          <a class="small link-secondary text-decoration-none" routerLink="/">View store</a>
          <span class="small text-muted">{{ account.user()?.fullName ?? 'Staff' }}</span>
          <button class="btn btn-sm btn-outline-secondary" type="button" (click)="account.logout()">
            Sign out
          </button>
        </div>
      </div>
    </nav>
  `
})
export class AdminNavbar {
  protected readonly account = inject(AccountService);

  readonly toggleSidebar = output<void>();
}
