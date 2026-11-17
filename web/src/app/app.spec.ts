import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders the sidebar brand and navigation', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.brand-text')?.textContent).toContain('Waypoint');
    expect(el.querySelectorAll('.sidebar-nav a.nav-item-link').length).toBe(4);
    TestBed.inject(HttpTestingController);
  });

  it('opens the shortcut help with "?"', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: '?' }));
    fixture.detectChanges();
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('#kb-title')?.textContent).toContain('Keyboard shortcuts');
  });
});
