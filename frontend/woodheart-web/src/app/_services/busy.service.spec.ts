import { BusyService } from './busy.service';

describe('BusyService', () => {
  let service: BusyService;

  beforeEach(() => {
    service = new BusyService();
  });

  it('is idle to begin with', () => {
    expect(service.isBusy()).toBe(false);
  });

  it('stays busy until every in-flight request has finished', () => {
    // The reason this is a counter and not a boolean. A product page fires the
    // product and the related-products calls together; if the first to return
    // cleared a boolean, the spinner would vanish while the page was still
    // loading.
    service.busy();
    service.busy();

    service.idle();
    expect(service.isBusy()).toBe(true);

    service.idle();
    expect(service.isBusy()).toBe(false);
  });

  it('never lets the count go negative', () => {
    // An unbalanced idle() must not push the counter below zero, because a
    // negative count makes the next genuine busy() fail to show anything.
    service.idle();
    service.idle();

    expect(service.isBusy()).toBe(false);

    service.busy();
    expect(service.isBusy()).toBe(true);
  });

  it('resets to idle', () => {
    service.busy();
    service.busy();

    service.reset();

    expect(service.isBusy()).toBe(false);
  });
});
