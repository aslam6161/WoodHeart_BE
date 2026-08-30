import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-footer',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer class="bg-light border-top mt-5 py-4">
      <div class="container">
        <div class="row gy-3">
          <div class="col-12 col-md-4">
            <h6 class="fw-semibold">WoodHeart</h6>
            <p class="text-muted small mb-0">
              Home interior furniture and interior design consultation, made in Bangladesh.
            </p>
          </div>

          <div class="col-6 col-md-4">
            <h6 class="fw-semibold">Shop</h6>
            <ul class="list-unstyled small mb-0">
              <li>
                <a class="link-secondary text-decoration-none" routerLink="/products">
                  All products
                </a>
              </li>
              <li>
                <a class="link-secondary text-decoration-none" routerLink="/consultation">
                  Consultation
                </a>
              </li>
            </ul>
          </div>

          <div class="col-6 col-md-4">
            <h6 class="fw-semibold">Help</h6>
            <ul class="list-unstyled small mb-0">
              <li>
                <a class="link-secondary text-decoration-none" routerLink="/contact">Contact us</a>
              </li>
            </ul>
          </div>
        </div>

        <hr />

        <p class="text-muted small mb-0">© {{ year }} WoodHeart. All rights reserved.</p>
      </div>
    </footer>
  `
})
export class Footer {
  protected readonly year = new Date().getFullYear();
}
