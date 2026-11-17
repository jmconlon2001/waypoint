import { Component, inject, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { Person } from '../core/models';

/** Free-text person field with autocomplete from the Person table. New names are created on save. */
@Component({
  selector: 'app-person-input',
  imports: [FormsModule],
  template: `
    <div class="autocomplete">
      <div class="input-icon">
        <span class="i i-user"></span>
        <input class="form-control" [id]="inputId" [ngModel]="value()" (ngModelChange)="onInput($event)" autocomplete="off"
               placeholder="Who? (optional)" (keydown)="onKey($event)" (focus)="onInput(value())" (blur)="close()" />
      </div>
      @if (open() && suggestions().length) {
        <ul class="autocomplete-list" role="listbox">
          @for (p of suggestions(); track p.id; let idx = $index) {
            <li role="option" [class.active]="idx === active()" (mousedown)="pick(p); $event.preventDefault()">{{ p.name }}</li>
          }
        </ul>
      }
    </div>
  `,
})
export class PersonInput {
  private readonly api = inject(ApiService);
  readonly value = model<string>('');
  inputId = 'person-input';
  protected readonly suggestions = signal<Person[]>([]);
  protected readonly open = signal(false);
  protected readonly active = signal(-1);
  private timer: ReturnType<typeof setTimeout> | undefined;

  onInput(v: string): void {
    this.value.set(v);
    clearTimeout(this.timer);
    this.timer = setTimeout(async () => {
      try {
        const list = await this.api.searchPeople(v ?? '');
        this.suggestions.set(list.filter((p) => p.name.toLowerCase() !== (v ?? '').trim().toLowerCase()).slice(0, 8));
        this.active.set(-1);
        this.open.set(true);
      } catch {
        this.suggestions.set([]);
      }
    }, 120);
  }

  onKey(e: KeyboardEvent): void {
    const n = this.suggestions().length;
    if (!this.open() || !n) return;
    if (e.key === 'ArrowDown') { this.active.set((this.active() + 1) % n); e.preventDefault(); }
    else if (e.key === 'ArrowUp') { this.active.set((this.active() - 1 + n) % n); e.preventDefault(); }
    else if (e.key === 'Enter' && this.active() >= 0) { this.pick(this.suggestions()[this.active()]); e.preventDefault(); }
    else if (e.key === 'Escape') { this.open.set(false); e.stopPropagation(); }
  }

  pick(p: Person): void {
    this.value.set(p.name);
    this.open.set(false);
  }

  close(): void {
    setTimeout(() => this.open.set(false), 120);
  }
}
