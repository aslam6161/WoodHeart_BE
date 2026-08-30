import { Component, ChangeDetectionStrategy, input, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AccountService } from '../../../_services/account.service';

interface NavItem {
  label: string;
  path: string;
  /** Null means every staff role sees it. */
  requiresAdmin?: boolean;
}

@Component({
  selector: 'app-admin-sidebar',
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <aside class="wh-sidebar bg-dark text-white" [class.wh-sidebar--collapsed]="collapsed()">
      <div class="p-3 border-bottom border-secondary">
        <span class="fw-semibold">{{ collapsed() ? 'WH' : 'WoodHeart' }}</span>
      </div>

      <nav class="nav flex-column p-2">
        @for (item of visibleItems(); track item.path) {
          <a
            class="nav-link text-white-50 px-2 py-2 rounded"
            routerLinkActive="active bg-secondary text-white"
            [routerLink]="item.path"
            [title]="item.label">
            {{ collapsed() ? item.label.charAt(0) : item.label }}
          </a>
        }
      </nav>
    </aside>
  `,
  styles: `
    .wh-sidebar {
      width: 230px;
      flex-shrink: 0;
      transition: width 0.15s ease;
    }

    .wh-sidebar--collapsed {
      width: 64px;
    }

    /* The sidebar is a navigation aid, not content. On a phone the admin gets
       the screen instead. */
    @media (max-width: 767.98px) {
      .wh-sidebar {
        display: none;
      }
    }
  `
})
export class AdminSidebar {
  private readonly account = inject(AccountService);

  readonly collapsed = input(false);

  private readonly items: NavItem[] = [
    { label: 'Dashboard', path: '/admin' },
    { label: 'Products', path: '/admin/products' },
    { label: 'Categories', path: '/admin/categories' },
    { label: 'Orders', path: '/admin/orders' },
    { label: 'Inventory', path: '/admin/inventory' },
    { label: 'Discounts', path: '/admin/discounts' },
    { label: 'Consultations', path: '/admin/consultations' },
    { label: 'Customers', path: '/admin/customers' },
    // Settings exposes payment credentials, so it is Admin-only rather than
    // staff-wide.
    { label: 'Settings', path: '/admin/settings', requiresAdmin: true }
  ];

  protected visibleItems(): NavItem[] {
    const isAdmin = this.account.isAdmin();

    return this.items.filter(item => !item.requiresAdmin || isAdmin);
  }
}
