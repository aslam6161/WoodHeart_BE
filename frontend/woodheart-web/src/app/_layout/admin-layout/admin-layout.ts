import { Component, ChangeDetectionStrategy, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { AdminNavbar } from '../adminComponents/admin-navbar/admin-navbar';
import { AdminSidebar } from '../adminComponents/admin-sidebar/admin-sidebar';
import { AdminFooter } from '../adminComponents/admin-footer/admin-footer';

/**
 * The admin shell: collapsible sidebar, top bar, content, footer.
 *
 * Everything under `/admin` is lazily loaded behind this layout and a role
 * guard, so a public visitor never downloads a byte of it.
 */
@Component({
  selector: 'app-admin-layout',
  imports: [RouterOutlet, AdminNavbar, AdminSidebar, AdminFooter],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wh-admin" [class.wh-admin--collapsed]="sidebarCollapsed()">
      <app-admin-sidebar [collapsed]="sidebarCollapsed()" />

      <div class="wh-admin__body">
        <app-admin-navbar (toggleSidebar)="toggleSidebar()" />

        <main class="wh-admin__content p-3 p-lg-4">
          <router-outlet />
        </main>

        <app-admin-footer />
      </div>
    </div>
  `,
  styles: `
    .wh-admin {
      display: flex;
      min-height: 100vh;
    }

    .wh-admin__body {
      display: flex;
      flex-direction: column;
      flex: 1;
      min-width: 0; /* lets wide tables scroll instead of stretching the layout */
    }

    .wh-admin__content {
      flex: 1;
    }
  `
})
export class AdminLayout {
  protected readonly sidebarCollapsed = signal(false);

  protected toggleSidebar(): void {
    this.sidebarCollapsed.update(collapsed => !collapsed);
  }
}
