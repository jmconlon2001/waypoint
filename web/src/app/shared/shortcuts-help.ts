import { Component, inject } from '@angular/core';
import { UiStore } from '../core/ui.store';
import { SHORTCUTS } from '../shortcuts';

@Component({
  selector: 'app-shortcuts-help',
  template: `
    <div class="sm-modal-backdrop" (click)="ui.shortcutsOpen.set(false)">
      <div class="sm-modal" role="dialog" aria-modal="true" aria-labelledby="kb-title" (click)="$event.stopPropagation()">
        <div class="sm-modal-header">
          <h3 id="kb-title"><span class="i i-keyboard"></span> Keyboard shortcuts</h3>
          <button type="button" class="icon-btn icon-btn-sm" (click)="ui.shortcutsOpen.set(false)" aria-label="Close"><span class="i i-x"></span></button>
        </div>
        <div class="sm-modal-body">
          <div class="shortcut-grid">
            @for (s of shortcuts; track s.label) {
              <span>@for (k of s.keys; track $index) { @if (k === '–') { <span class="text-muted mx-1">to</span> } @else { <kbd>{{ k }}</kbd> } }</span>
              <span>{{ s.label }}</span>
            }
          </div>
          <p class="form-text mt-3 mb-0">Shortcuts are ignored while you type in a field. In the interruption form, 1-6 pick the source and Enter goes back to work.</p>
        </div>
      </div>
    </div>
  `,
})
export class ShortcutsHelp {
  protected readonly ui = inject(UiStore);
  protected readonly shortcuts = SHORTCUTS;
}
