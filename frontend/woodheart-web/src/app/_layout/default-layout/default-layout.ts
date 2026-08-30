import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Nav } from '../../nav/nav';
import { Footer } from '../../footer/footer';

/**
 * The public storefront shell: header, content, footer.
 *
 * Kept separate from the admin shell so the two can diverge — and so the
 * admin's chrome is never downloaded by a customer browsing products.
 */
@Component({
  selector: 'app-default-layout',
  imports: [RouterOutlet, Nav, Footer],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-nav />

    <main class="wh-main">
      <router-outlet />
    </main>

    <app-footer />
  `,
  styles: `
    .wh-main {
      min-height: 60vh;
    }
  `
})
export class DefaultLayout {}
