import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AccountService } from '../_services/account.service';

/** The storefront header. */
@Component({
  selector: 'app-nav',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="navbar navbar-expand-lg navbar-light bg-white border-bottom sticky-top">
      <div class="container">
        <a class="navbar-brand fw-semibold" routerLink="/">WoodHeart</a>

        <button
          class="navbar-toggler"
          type="button"
          [attr.aria-expanded]="menuOpen()"
          aria-label="Toggle navigation"
          (click)="toggleMenu()">
          <span class="navbar-toggler-icon"></span>
        </button>

        <div class="collapse navbar-collapse" [class.show]="menuOpen()">
          <ul class="navbar-nav me-auto mb-2 mb-lg-0">
            <li class="nav-item">
              <a class="nav-link" routerLink="/products" routerLinkActive="active">Shop</a>
            </li>
            <li class="nav-item">
              <a class="nav-link" routerLink="/consultation" routerLinkActive="active">
                Consultation
              </a>
            </li>
          </ul>

          <ul class="navbar-nav">
            @if (account.isStaff()) {
              <li class="nav-item">
                <a class="nav-link" routerLink="/admin">Admin</a>
              </li>
            }

            @if (account.isAuthenticated()) {
              <li class="nav-item">
                <a class="nav-link" routerLink="/account">
                  {{ account.user()?.fullName ?? 'Account' }}
                </a>
              </li>
              <li class="nav-item">
                <button class="btn btn-link nav-link" type="button" (click)="account.logout()">
                  Sign out
                </button>
              </li>
            } @else {
              <li class="nav-item">
                <a class="nav-link" routerLink="/login">Sign in</a>
              </li>
            }

            <li class="nav-item">
              <a class="nav-link" routerLink="/cart" aria-label="Basket">Basket</a>
            </li>
          </ul>
        </div>
      </div>
    </nav>
  `
})
export class Nav {
  protected readonly account = inject(AccountService);
  protected readonly menuOpen = signal(false);

  protected toggleMenu(): void {
    this.menuOpen.update(open => !open);
  }
}
