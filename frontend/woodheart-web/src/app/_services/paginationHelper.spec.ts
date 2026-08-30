import { HttpParams } from '@angular/common/http';
import { appendIfPresent, getPaginationHeaders } from './paginationHelper';

describe('getPaginationHeaders', () => {
  it('sends both paging values', () => {
    const params = getPaginationHeaders(2, 24);

    expect(params.get('pageNumber')).toBe('2');
    expect(params.get('pageSize')).toBe('24');
  });
});

describe('appendIfPresent', () => {
  it('appends a real value', () => {
    const params = appendIfPresent(new HttpParams(), 'categoryId', 7);

    expect(params.get('categoryId')).toBe('7');
  });

  it('omits null, undefined and empty string', () => {
    // The distinction that matters: `?categoryId=` is not the same as omitting
    // the parameter. Model binding sees an empty string, the query filters on
    // nothing, and the customer gets zero results from a filter they never
    // applied.
    for (const empty of [null, undefined, '']) {
      const params = appendIfPresent(new HttpParams(), 'categoryId', empty);

      expect(params.has('categoryId')).toBe(false);
    }
  });

  it('keeps a legitimate zero and a false', () => {
    // Zero is a real minimum price and false is a real "not featured" filter.
    // A naive falsy check would drop both.
    expect(appendIfPresent(new HttpParams(), 'minPrice', 0).get('minPrice')).toBe('0');
    expect(appendIfPresent(new HttpParams(), 'featured', false).get('featured')).toBe('false');
  });
});
